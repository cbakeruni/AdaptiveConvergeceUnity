using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class MasterAlgorithm : MonoBehaviour
{
    //globals from disk
    public static int numReactors; // batch size
    public static float manufactureCost; //ignore, here for simulation
    public static float weeklyOpCost; //ignore, here for simulation
    public static float maxDuration; // days allowed in campaign
    public static float serumPrice; //ignore, here for simulation
    public static float systemFluidVolume; //ignore, here for simulation

    // logs
    public readonly List<List<ReactorInput>> previousSets = new();
    private readonly List<List<int>> resultSets = new();
    private readonly List<List<float>> costSets = new();

    // controls
    public List<ControlParameter.Control> allControls = new();

    // adaptive state
    private int batchIndex = 0; // generation counter
    public float elapsedDays = 0f;
    private float[] uncertainty;           // mutation lerp factor per gene
    private float[] qGainSum;           // sum of delta quality accrued by mutating gene i
    private int[]   qGainTrials;        // number of times gene i was mutated
    private float[] gradSum;     //gradient of change in activity and change in performance
    private int[]   gradCnt;    //number of times gradient was calculated
    private readonly HashSet<string> seenFingerprints = new();

    public SubmitBatchResultsUI ui;
    
    [Serializable]
    private sealed class ParetoEntry
    {
        public int q;
        public float c;
        public int b;
        public int i;
        public ParetoEntry(int q, float c, int b, int i)
        {
            this.q = q; this.c = c; this.b = b; this.i = i;
        }
    }

    [Serializable]
    private sealed class AlgorithmState
    {
        public List<List<ReactorInput>> previousSets;
        public List<List<int>>          resultSets;
        public List<List<float>>        costSets;
        public int                      batchIndex;
        public float                    elapsedDays;
        public float[]                  uncertainty;
        public float[]                  qGainSum;
        public int[]                    qGainTrials;
        public float[]                  gradSum;
        public int[]                    gradCnt;
        public List<ParetoEntry>        paretoFront;
    }

    private const string InstructionFileName = "instructions";
    private readonly List<ParetoEntry> paretoFront = new();

    public static MasterAlgorithm i;

    // Tunable parameter
    [Header("Play‑mode stubs")] public int randomSeed = 42;
    private System.Random rng;
    const float QUALITY_SCALE   = 2f;  
    const float EXPLORATION_BIAS = 0.2f; // base probability to try an unseen gene
    private const float DECAY = 0.95f; //How quickly data is forgotton, to accomodate more exploraton and not forget genes

    public static int nextID = 0;
    private float crossoverRate = 0.7f;
    
    List<ReactorInput> bestReactors = new();
    
    struct Change
    {
        public int indexOfControl;
        public float factor;
        public bool increase;
    }

    private void Awake()
    {
        i = this;
    }


    ReactorInput Gen1Reactor()
    {
        Debug.Log("Generating gen1 reactor");
        List<SensorStruct> ss = new List<SensorStruct>();
        List<ExcreterStruct> exs = new List<ExcreterStruct>();
        List<EliminatorStruct> els = new List<EliminatorStruct>();
        List<SystemControl> cs = new List<SystemControl>();
        for (int i = 0; i < allControls.Count; i++)
        {
            ControlParameter.Control c = allControls[i];
            switch (allControls[i].type)
            {
                case 0:
                    float min;
                    float max;
                    switch (allControls[i].objective)
                    {
                        case ControlParameter.Control.Objective.maximise:
                            min = c.values.z + (c.values.y - c.values.z) * 0.5f;
                            max = Mathf.Infinity;
                            break;
                        case ControlParameter.Control.Objective.minimise:
                            min = 0f;
                            max = c.values.z + (c.values.x - c.values.z) * 0.5f;
                            break;
                        case ControlParameter.Control.Objective.range:
                        default:
                            min = c.values.z + (c.values.x - c.values.z) * 0.75f;
                            max = c.values.z + (c.values.y - c.values.z) * 0.75f;
                            break;
                    }
                    ss.Add(new SensorStruct(c.name,min,max,false,c.objective));
                    break;
                case 1:
                    float rate = c.values.x + c.values.z / 2f; //set to default. It is the 1st and 3rd value of the v3 that are set as the range for excreters.
                    exs.Add(new ExcreterStruct(c.name,rate,false,c.supplyCost));
                    break;
                case 2:
                    els.Add(new EliminatorStruct(c.name));
                    break;
                default:
                    cs.Add(new SystemControl(c.name, c.values.z));
                    break;
            }
        }
        return FinaliseReactor(cs, ss, exs, els, new ReactorInput());
    }

    bool KeepProb(int i)
    {
        // If never touched this gene we know nothing about it – default to 1/numControls chance
        if (qGainTrials[i] == 0) return rng.NextDouble() < 1f/allControls.Count;

        float mean = qGainSum[i] / qGainTrials[i];            // empirical deltaq
        // squish mean into [-1, 1] → probability in [0,1]
        float p = (float)(0.5f + 0.5f * Math.Tanh(mean / QUALITY_SCALE));
        return rng.NextDouble() < p;
    }

    bool AcquireProb(int i)
    {
        float exploit = 0f;
        if (qGainTrials[i] > 0)
        {
            float mean = qGainSum[i] / qGainTrials[i];
            exploit = Mathf.Clamp01(mean / QUALITY_SCALE);    // positive means desirable. Useful for starting things off in good directions.
        }

        //  Exploration term  bigger when the gene has been tried less often, prevents genes being left behind.
        float explore = 1f / Mathf.Sqrt(1f + qGainTrials[i]);

        // blend always keeps a little exploration so we do not get stuck
        float p = EXPLORATION_BIAS * explore + (1f - EXPLORATION_BIAS) * exploit;
        return rng.NextDouble() < p;
    }

    //REQUESTS WHICH CONTROL SHOULD BE INCORPORATED IN THIS EXPERIMENT, REQUESTS WHICH CHANGES SHOULD BE APPLIED, GIVES A MODIFYFROM CONFIGURATION TO START FROM (NOT-BLANK).
    ReactorInput MutateReactor(List<Change> changes, ReactorInput modifyFrom, bool evolution, ReactorInput? parentOverrideA = null, ReactorInput? parentOverrideB = null)
    {
        Debug.Log("Mutating reactor");
        List<SystemControl> controls = new List<SystemControl>();
        List<SensorStruct> sensors = new List<SensorStruct>();
        List<ExcreterStruct> excreters = new List<ExcreterStruct>();
        List<EliminatorStruct> eliminators = new List<EliminatorStruct>();

        List<ControlParameter.Control> cs = new List<ControlParameter.Control>();

        if (evolution) //RANDOM + INFORMED MUTATION
        {
            for (int i = 0; i < allControls.Count; i++)
            {
                if (modifyFrom.controlIndices.Contains(i))
                {
                    if (KeepProb(i))
                    {
                        cs.Add(allControls[i]);
                    }
                }
                else
                {
                    if (AcquireProb(i))
                    {
                        cs.Add(allControls[i]);
                    }
                }
            }
        }
        else //CHANGES INFORM THE DESIGN
        {
            for (int i = 0; i < allControls.Count; i++)
            {
                if (modifyFrom.controlIndices.Contains(i))
                {
                    cs.Add(allControls[i]); //KEEP ALL THE CONTROLS FROM THE BEST REACTOR
                }
                else if (changes.Any(x => x.indexOfControl == i))
                {
                    cs.Add(allControls[i]); //AND ANY SUGGESTED CHANGES
                }
                else  if (AcquireProb(i))
                {
                    cs.Add(allControls[i]); //AND A SMALL CHANCE TO TRY ANOTHER ONE
                }
            }
        }
      
        
        foreach (ControlParameter.Control c in cs)
        {
            int ind = allControls.IndexOf(c);
            Change change = new Change();
            float min;
            float max;
            switch (c.type)
            {
                case 0: //sensor

                    SensorStruct s = modifyFrom.sensors.FirstOrDefault(x => x.name == c.name);
                    if (s.name == c.name) //If sensor existed in the ModifyFromConfig, load these previous values
                    {
                        max = s.maxValue;
                        min = s.minValue;
                    }
                    else
                    {
                        switch
                            (c.objective) //OTHERWISE LOAD THE DEFAULT OPERATING RANGES FOR THE SENSOR BASED ON OBJECTIVE (USING USER-LOGGED CONTROL INVESTIGATION RANGE VECTOR3 IN THE CONTROLPARAMETER.CONTROL)
                        {
                            case ControlParameter.Control.Objective.maximise:
                                min = c.values.z + (c.values.y - c.values.z) * 0.5f;
                                max = Mathf.Infinity;
                                break;
                            case ControlParameter.Control.Objective.minimise:
                                min = 0f;
                                max = c.values.z + (c.values.x - c.values.z) * 0.5f;
                                break;
                            case ControlParameter.Control.Objective.range:
                            default:
                                min = c.values.z + (c.values.x - c.values.z) * 0.75f;
                                max = c.values.z + (c.values.y - c.values.z) * 0.75f;
                                break;
                        }
                    }

                    if (changes.Any(x => x.indexOfControl == ind)) //IF A CHANGE IS REQUESTED, CHANGE THE RANGES
                    {
                        change = changes.First(x => x.indexOfControl == ind);
                        switch (change.increase)
                        {
                            case true when c.objective == ControlParameter.Control.Objective.minimise:
                                min = 0f;
                                max = Mathf.Lerp(max, c.values.x, uncertainty[ind] * change.factor);
                                break;
                            case false when c.objective == ControlParameter.Control.Objective.minimise:
                                min = 0f;
                                max = Mathf.Lerp(max, c.values.y, uncertainty[ind] * change.factor);
                                break;
                            case true when c.objective == ControlParameter.Control.Objective.maximise:
                                min = Mathf.Lerp(min, c.values.x, uncertainty[ind] * change.factor);
                                max = Mathf.Infinity;
                                break;
                            case false when c.objective == ControlParameter.Control.Objective.maximise:
                                min = Mathf.Lerp(min, c.values.y, uncertainty[ind] * change.factor);
                                max = Mathf.Infinity;
                                break;
                            case true when c.objective == ControlParameter.Control.Objective.range:
                                min = Mathf.Lerp(min, c.values.z, uncertainty[ind] * 0.5f * change.factor);
                                max = Mathf.Lerp(max, c.values.z, uncertainty[ind] * 0.5f * change.factor);
                                break;
                            case false when c.objective == ControlParameter.Control.Objective.range:
                                min = Mathf.Lerp(min, c.values.x, uncertainty[ind] * 0.5f * change.factor);
                                max = Mathf.Lerp(max, c.values.y, uncertainty[ind] * 0.5f * change.factor);
                                break;
                        }
                    }

                    sensors.Add(new SensorStruct(c.name,
                        min, //Min value
                        max, //Max value
                        false, //hasExcreter filled in later
                        c.objective)); //Objective)

                    break;
                case 1: //excreter
                    ExcreterStruct e = modifyFrom.excreters.FirstOrDefault(x => x.name == c.name);
                    float rate = e.name == c.name ? e.dailyRate : (c.values.x + c.values.z) / 2f; //If excreter existed in the ModifyFromConfig, load these previous values, if no excreter otherwise.
                    if (changes.Any(x => x.indexOfControl == ind)) //IF A CHANGE IS REQUESTED, UPDATE RANGE
                    {
                        change = changes.First(x => x.indexOfControl == ind);
                        switch (change.increase)
                        {
                            case true:
                                rate = Mathf.Lerp(rate, c.values.z, uncertainty[ind] * change.factor); //increase towards upper bound
                                break;
                            case false:
                                rate = Mathf.Lerp(rate, c.values.x, uncertainty[ind] * change.factor); //decrease towards lower bound
                                break;
                        }
                    }

                    excreters.Add(new ExcreterStruct(c.name, rate, false, c.supplyCost));
                    break;
                case 2: //eliminator NOTHING WRITTEN, IS PASSIVE
                    eliminators.Add(new EliminatorStruct(c.name));
                    break;
                default:
                    SystemControl sc = modifyFrom.systemControls.FirstOrDefault(x => x.name == c.name);
                    float val; // unit per day.
                    val = sc.name == c.name ? sc.value : c.values.z; //If system control existed in the ModifyFromConfig, load these previous values if no control otherwise.
                    if (changes.Any(x => x.indexOfControl == ind)) //IF A CHANGE IS REQUESTED, UPDATE RANGE
                    {
                        change = changes.First(x => x.indexOfControl == ind);
                        switch (change.increase)
                        {
                            case true:
                                val = Mathf.Lerp(val, c.values.y, uncertainty[ind] * change.factor); //increase towards upper bound
                                break;
                            case false:
                                val = Mathf.Lerp(val, c.values.x, uncertainty[ind] * change.factor); //decrease towards lower bound
                                break;
                        }
                    }
                    controls.Add(new SystemControl(c.name, val));
                    break;
            }
        }

        if (parentOverrideA.HasValue && parentOverrideB.HasValue)
        {
            ReactorInput r = FinaliseReactor(controls, sensors, excreters, eliminators, modifyFrom);
            return new ReactorInput(r.initCost, r.contCost,
                controls, sensors, excreters, eliminators, modifyFrom.controlIndices, parentOverrideA.Value, parentOverrideB.Value);
        }
        if (parentOverrideA.HasValue)
        {
            ReactorInput r = FinaliseReactor(controls, sensors, excreters, eliminators, modifyFrom);
            return new ReactorInput(r.initCost, r.contCost,
                controls, sensors, excreters, eliminators, modifyFrom.controlIndices, parentOverrideA.Value);
        }
        return FinaliseReactor(controls,sensors,excreters,eliminators, modifyFrom);
    }

    List<Change> GenerateRandomChanges()
    {
        List<Change> changes = new();
        for(int i = 0; i < allControls.Count; i++)
        {
            if (rng.NextDouble() < 0.5f)
            {
                changes.Add(new Change
                {
                    indexOfControl = i,
                    factor =  (float)rng.NextDouble(),
                    increase = rng.NextDouble() < 0.5f
                });
            }
        }
        return changes;
    }
    //merge two lists of value‑type structs keyed by the "name" field
    private List<T> MergeList<T>(IEnumerable<T> listA,
        IEnumerable<T> listB)
        where T : struct
    {
        // Grab the public field "name" via cached reflection – works for all three structs
        var nameField = typeof(T).GetField("name");
        if(nameField == null)
            throw new InvalidOperationException($"Type {typeof(T)} lacks a public 'name' field");

        // Index parents by name
        Dictionary<string,T> dictA = listA.ToDictionary(x => (string)nameField.GetValue(x), x => x);
        Dictionary<string,T> dictB = listB.ToDictionary(x => (string)nameField.GetValue(x), x => x);

        HashSet<string> allKeys = new HashSet<string>(dictA.Keys);
        allKeys.UnionWith(dictB.Keys);

        List<T> result = new();

        foreach(string key in allKeys)
        {
            bool inA = dictA.TryGetValue(key, out var aVal);
            bool inB = dictB.TryGetValue(key, out var bVal);

            if(inA && inB)
            {
                // Present in both – choose one whole struct
                result.Add(rng.NextDouble() < 0.5 ? aVal : bVal);
            }
            else
            {
                result.Add(inA ? aVal : bVal);
            }
        }
        return result;
    }

    
    private ReactorInput Crossover(ReactorInput parentA, ReactorInput parentB)
    {
        Debug.Log("Doing crossover");
        var controls = MergeList(parentA.systemControls, parentB.systemControls);
        var sensors = MergeList(parentA.sensors, parentB.sensors);
        var excreters  = MergeList(parentA.excreters,  parentB.excreters);
        var eliminators= MergeList(parentA.eliminators,parentB.eliminators);
        return FinaliseReactor(controls, sensors, excreters, eliminators, parentA,parentB);
    }

    ReactorInput FinaliseReactor(List<SystemControl> cs, List<SensorStruct> ss, List<ExcreterStruct> exs, List<EliminatorStruct> els, ReactorInput parent, ReactorInput? parentB = null)
    {
        List<int> indices = new List<int>();
        if (cs != null)
        {
            foreach (SystemControl a in cs)
            {
                indices.Add(allControls.IndexOf(allControls.First(x => x.name == a.name && x.type > 3)));
            }
        }
        if (ss != null)
        {
            foreach (SensorStruct b in ss)
            {
                indices.Add(allControls.IndexOf(allControls.First(x => x.name == b.name && x.type == 0)));
            }
        }
        if (exs != null)
        {
            foreach (ExcreterStruct c in exs)
            {
                indices.Add(allControls.IndexOf(allControls.First(x=> x.name == c.name && x.type == 1)));
            }
        }
        if (els != null)
        {
            foreach (EliminatorStruct d in els)
            {
                indices.Add(allControls.IndexOf(allControls.First(x=> x.name == d.name && x.type == 2)));
            }
        }
        for(int i = 0; i < ss.Count; i++)
        {
            ss[i] = new SensorStruct(ss[i].name,
                ss[i].minValue,
                ss[i].maxValue,
                exs.Any(x=>x.name == ss[i].name),
                ss[i].objective);
        }
        for(int i = 0; i< exs.Count; i++)
        {
            exs[i] = new ExcreterStruct(exs[i].name,
                exs[i].dailyRate,
                ss.Any(x=>x.name == exs[i].name), exs[i].replenishCost);
        }
        
        ReactorInput input = new ReactorInput(
            0f, 0f, cs, ss, exs, els, null);
        
        (float initCost,float contCost) = GetCostsFromReactor(input);

        if (parentB != null)
        {
            return new ReactorInput(
                initCost, contCost, cs, ss, exs, els, indices, parent,parentB.Value);
        }
        return new ReactorInput(
            initCost, contCost, cs, ss, exs, els, indices, parent);
    }
    
    ReactorInput CreateComparisonReactor(List<SystemControl> cs, List<SensorStruct> ss, List<ExcreterStruct> exs, List<EliminatorStruct> els)
    {
        List<int> indices = new List<int>();
        if (cs != null)
        {
            foreach (SystemControl a in cs)
            {
                indices.Add(allControls.IndexOf(allControls.First(x => x.name == a.name && x.type > 3f)));
            }
        }
        if (ss != null)
        {
            foreach (SensorStruct b in ss)
            {
                indices.Add(allControls.IndexOf(allControls.First(x => x.name == b.name && x.type == 0)));
            }
        }
        if (exs != null)
        {
            foreach (ExcreterStruct c in exs)
            {
                indices.Add(allControls.IndexOf(allControls.First(x=> x.name == c.name && x.type == 1)));
            }
        }
        if (els != null)
        {
            foreach (EliminatorStruct d in els)
            {
                indices.Add(allControls.IndexOf(allControls.First(x=> x.name == d.name && x.type == 2)));
            }
        }
        
        return new ReactorInput(
            0f, 0f, cs, ss, exs, els, indices);
        
    }
    
    (float,float) GetCostsFromReactor(ReactorInput r)
    {
        float initCost = 0f;
        float contCost = 0f;
        foreach (SensorStruct s in r.sensors)
        {
            //find the name of the control in allControls
            ControlParameter.Control c = allControls.FirstOrDefault(x => x.name == s.name && x.type == 0);
            if (c.name == null) continue; // not found
            initCost += c.startupCost;
            contCost += c.continuationCost;
        }
        foreach (ExcreterStruct e in r.excreters)
        {
            //find the name of the control in allControls
            ControlParameter.Control c = allControls.FirstOrDefault(x => x.name == e.name && x.type == 1);
            if (c.name == null) continue; // not found
            initCost += c.startupCost;
            contCost += c.continuationCost;
        }
        foreach (EliminatorStruct e in r.eliminators)
        {
            //find the name of the control in allControls
            ControlParameter.Control c = allControls.FirstOrDefault(x => x.name == e.name && x.type == 2);
            if (c.name == null) continue; // not found
            initCost += c.startupCost;
            contCost += c.continuationCost;
        }
        return (initCost, contCost);
    }

    [Serializable]
    public struct ReactorInput
    {
        public float initCost;
        public float contCost;
        public List<SystemControl> systemControls;
        public List<SensorStruct> sensors;
        public List<ExcreterStruct> excreters;
        public List<EliminatorStruct> eliminators;
        public List<int> controlIndices;
        public int score;
        public float costResult;

        public List<SystemControl> parentControls;
        public List<SensorStruct> parentSensors;
        public List<ExcreterStruct> parentExcreters;
        public List<EliminatorStruct> parentEliminators;
        public int parentScore;
        public float parentCostResult;
        
        public List<SystemControl> parentControls2;
        public List<SensorStruct> parentSensors2;
        public List<ExcreterStruct> parentExcreters2;
        public List<EliminatorStruct> parentEliminators2;
        public int parent2Score;
        public float parent2CostResult;
        private int ID;

        public ReactorInput(float initCost, float contCost, List<SystemControl> systemControls, List<SensorStruct> sensors, List<ExcreterStruct> excreters,
            List<EliminatorStruct> eliminators, List<int> controlIndices)
        {
            this.initCost = initCost;
            this.contCost = contCost;
            this.systemControls = systemControls;
            this.sensors = sensors;
            this.excreters = excreters;
            this.eliminators = eliminators;
            this.controlIndices = controlIndices;
            ID = nextID++;
            score = 0;
            this.costResult = 0;
           
            parentControls = null;
            parentSensors = null;
            parentExcreters = null;
            parentEliminators = null;
            parentScore = 0;
            parentCostResult = 0;
            
            parentControls2 = null;
            parentSensors2 = null;
            parentExcreters2 = null;
            parentEliminators2 = null;
            parent2Score = 0;
            parent2CostResult = 0;
        }

        public ReactorInput(ReactorInput prev, int score, float costResult)
        {
            initCost = prev.initCost;
            contCost = prev.contCost;
            systemControls = prev.systemControls;
            sensors = prev.sensors;
            excreters = prev.excreters;
            eliminators = prev.eliminators;
            controlIndices = prev.controlIndices;
            ID = prev.ID;
            this.score = score;
            this.costResult = 0;
            
            parentControls = prev.parentControls;
            parentSensors = prev.parentSensors;
            parentExcreters = prev.parentExcreters;
            parentEliminators = prev.parentEliminators;
            parentScore = prev.score;
            parentCostResult = prev.costResult;
            
            parentControls2 = prev.parentControls2;
            parentSensors2 = prev.parentSensors2;
            parentExcreters2 = prev.parentExcreters2;
            parentEliminators2 = prev.parentEliminators2;
            parent2Score = prev.parent2Score;
            parent2CostResult = prev.parent2CostResult;
        }
        
        public ReactorInput(float initCost, float contCost, List<SystemControl> systemControls, List<SensorStruct> sensors, List<ExcreterStruct> excreters,
            List<EliminatorStruct> eliminators, List<int> controlIndices, ReactorInput parent)
        {
            this.initCost = initCost;
            this.contCost = contCost;
            this.systemControls = systemControls;
            this.sensors = sensors;
            this.excreters = excreters;
            this.eliminators = eliminators;
            this.controlIndices = controlIndices;
            ID = nextID++;
            score = 0;
            costResult = 0;
            parentControls = parent.systemControls;
            parentSensors = parent.sensors;
            parentExcreters = parent.excreters;
            parentEliminators = parent.eliminators;
            parentScore = parent.score;
            parentCostResult = parent.costResult;
            parentControls2 = null;
            parentSensors2 = null;
            parentExcreters2 = null;
            parentEliminators2 = null;
            parent2Score = 0;
            parent2CostResult = 0;
        }
        
        public ReactorInput(float initCost, float contCost, List<SystemControl> systemControls, List<SensorStruct> sensors, List<ExcreterStruct> excreters,
            List<EliminatorStruct> eliminators, List<int> controlIndices, ReactorInput parent, ReactorInput parentB)
        {
            this.initCost = initCost;
            this.contCost = contCost;
            this.systemControls = systemControls;
            this.sensors = sensors;
            this.excreters = excreters;
            this.eliminators = eliminators;
            this.controlIndices = controlIndices;
            ID = nextID++;
            score = 0;
            costResult = 0;
            parentControls = parent.systemControls;
            parentSensors = parent.sensors;
            parentExcreters = parent.excreters;
            parentEliminators = parent.eliminators;
            parentScore = parent.score;
            parentCostResult = parent.costResult;
            parentControls2 = parentB.systemControls;
            parentSensors2 = parentB.sensors;
            parentExcreters2 = parentB.excreters;
            parentEliminators2 = parentB.eliminators;
            parent2Score = parentB.score;
            parent2CostResult = parentB.costResult;
        }
        
        
    }

    [Serializable]
    public struct SystemControl
    {
        public string name;
        public float value;

        public SystemControl(string name, float value)
        {
            this.name = name;
            this.value = value;
        }
    }

    [Serializable]
    public struct SensorStruct
    {
        public string name;
        public float minValue;
        public float maxValue;
        public bool hasExcreter;

        public ControlParameter.Control.Objective
            objective; //DETREIMENTAL VARIABLES WE WANT MINIMISED, NON DETREMENTAL WE WANT MAXIMISED. THE V3 SERVES SO AS TO INIT SENSORS APPROPRIATELY AND SET EXPERIMENTAL RANGE

        public SensorStruct(string name, float minValue, float maxValue, bool hasExcreter,
            ControlParameter.Control.Objective objective)
        {
            this.name = name;
            this.minValue = minValue;
            this.maxValue = maxValue;
            this.hasExcreter = hasExcreter;
            this.objective = objective;
        }
    }

    [Serializable]
    public struct ExcreterStruct
    {
        public string name;
        public float dailyRate; //IF HASSENSOR: SERVES AS MAX RATE. //IF NOT SENSOR: SERVES AS CONSTANT RATE.
        public bool hasSensor; //THIS IS JUST HERE FOR SIMULATION COSTS. IRRELEVANT IN THIS CLASS.
        public float replenishCost; //THIS IS JUST HERE FOR SIMULATION COSTS. IRRELEVANT IN THIS CLASS.

        public ExcreterStruct(string name, float dailyRate, bool hasSensor, float replenishCost)
        {
            this.name = name;
            this.dailyRate = dailyRate;
            this.hasSensor = hasSensor;
            this.replenishCost = replenishCost;
        }
    }

    [Serializable]
    public struct EliminatorStruct
    {
        public string name;

        public EliminatorStruct(string name)
        {
            this.name = name;
        }
    }
    
    void Start()
    {
        Debug.Log("Starting Master Algorithm");
        rng = new System.Random(randomSeed);
        if (!SaveManager.Load(StartMenu.password, out allControls, out var g))
        {
            UIManager.Message("Error Loading File");
            LeanTween.delayedCall(3f, () => SceneManager.LoadScene(0));
            return;
        }

        numReactors = (int)g[0]; //ALWAYS GREATER OR EQUAL TO 16
        manufactureCost = g[1];
        weeklyOpCost = g[2];
        maxDuration = g[3];
        serumPrice = g[4];
        systemFluidVolume = g[5];

        uncertainty    = Enumerable.Repeat(1f, allControls.Count).ToArray();
        qGainSum    = new float[allControls.Count];
        qGainTrials = new int[allControls.Count];
        gradSum     = new float[allControls.Count];
        gradCnt     = new int[allControls.Count];

        //  Persistent‑run initialisation
        if (StartMenu.newExperiment)
        {
            // First ever generation seed an initial batch
            Debug.Log("No load, creating gen1 batch");
            previousSets.Clear();
            var firstBatch = LHCGaussianReactor(numReactors).ToList();
            previousSets.Add(firstBatch);
            SaveSnapshot();
        }
        else
        {
            if(!LoadSnapshot())
            {
                UIManager.Message("Error Loading File");
                LeanTween.delayedCall(3f, () => SceneManager.LoadScene(0));
                return;
            }
        }
        
        // external UI should call SubmitBatchResults()
        if (StartMenu.simulation)
        {
            Debug.Log("Simulation");
            ui.gameObject.SetActive(false);
            Simulation.i.Init();
        }
        else
        {
            Debug.Log("Preparing next prompt");
            ui.gameObject.SetActive(true);
            ui.PrepareNextPrompt();
        }
    }
    
    //  Utility: Ensure the ReactorInput is unique. If not, nudge one random gene
    private ReactorInput EnsureUnique(ReactorInput candidate, ReactorInput? A, ReactorInput? B)
    {
        const int MAX_TRIES = 20;

        for (int attempt = 0; attempt < MAX_TRIES; attempt++)
        {
            string fp = Fingerprint(candidate);
            if (seenFingerprints.Add(fp))          // HashSet.Add → false if already present
                return candidate;                  // NEW!  All done.

            // ── duplicate ──> randomly tweak ONE control ------------------------
            int i = rng.Next(allControls.Count);   // pick a gene index

            var tweak = new List<Change> {
                new Change {
                    indexOfControl = i,
                    factor         = (float)rng.NextDouble(),
                    increase       = rng.NextDouble() < 0.5
                }
            };
            candidate = MutateReactor(tweak, candidate, false, A, B);  // mutate *in place*
        }

        Debug.LogWarning("EnsureUnique exceeded max tries; returning last variant");
        return candidate;    // rare fall-through
    }
    
    // Utility: Which control-parameter indices differ between reactors a and b 
    private IEnumerable<int> DiffIndices(ReactorInput a, ReactorInput b)
    {
        const float EPS = 1e-4f;             // float equality guard

        // Fast name-lookup tables for the compound collections
        var aSensors = a.sensors   .ToDictionary(s => s.name);
        var bSensors = b.sensors   .ToDictionary(s => s.name);

        var aEx      = a.excreters .ToDictionary(e => e.name);
        var bEx      = b.excreters .ToDictionary(e => e.name);

        var aElim    = new HashSet<string>(a.eliminators.Select(e => e.name));
        var bElim    = new HashSet<string>(b.eliminators.Select(e => e.name));

        var aC = a.systemControls.ToDictionary(s => s.name);
        var bC = b.systemControls.ToDictionary(s => s.name);
        
        for (int i = 0; i < allControls.Count; i++)
        {
            ControlParameter.Control c = allControls[i];

            switch (c.type)
            {
                //  Sensors
                case 0:
                    bool sa = aSensors.TryGetValue(c.name, out var sA);
                    bool sb = bSensors.TryGetValue(c.name, out var sB);

                    if (sa != sb ||
                        (sa && (Mathf.Abs(sA.minValue - sB.minValue) > EPS ||
                                Mathf.Abs(sA.maxValue - sB.maxValue) > EPS)))
                        yield return i;
                    break;

                //  Excreters
                case 1:
                    bool ea = aEx.TryGetValue(c.name, out var eA);
                    bool eb = bEx.TryGetValue(c.name, out var eB);

                    if (ea != eb ||
                        (ea && Mathf.Abs(eA.dailyRate - eB.dailyRate) > EPS))
                        yield return i;
                    break;

                //  Eliminators (presence only)
                case 2:
                    if (aElim.Contains(c.name) != bElim.Contains(c.name))
                        yield return i;
                    break;

                //  System controls (types 4...18)
                default:
                {
                    bool ac = aC.TryGetValue(c.name, out var c1);
                    bool bc = bC.TryGetValue(c.name, out var c2);

                    if (ac != bc ||
                        (ac && Mathf.Abs(c1.value - c2.value) > EPS))
                        yield return i;

                    break;
                }
            }
        }
    }
    
    private List<ReactorInput> GenerateEvolutionaryBatch(float crossRate)
    {
        Debug.Log("Generating evo-batch");
        List<ReactorInput> batch = new();

        for (int i = 0; i < numReactors; i++)
        {
            bool doCrossover = rng.NextDouble() < crossRate && bestReactors.Count > 1;

            if (doCrossover)
            {
                // pick two *different* indices
                int idxA = rng.Next(bestReactors.Count);
                int idxB;
                if (bestReactors.Count == 2)
                {
                    idxB = 1 - idxA;                      
                }
                else
                {
                    do { idxB = rng.Next(bestReactors.Count); }
                    while (idxB == idxA);
                }

                ReactorInput parentA = bestReactors[idxA];
                ReactorInput parentB = bestReactors[idxB];
                ReactorInput r = Crossover(parentA, parentB);
                batch.Add(EnsureUnique(MutateReactor(GenerateRandomChanges(), r, true, parentA, parentB),parentA,parentB));
            }
            else
            {
                // mutation only
                ReactorInput parent = bestReactors[rng.Next(bestReactors.Count)];
                batch.Add(EnsureUnique(MutateReactor(GenerateRandomChanges(), parent, true),parent,null));
            }
        }
        return batch;
    }
    
    float Activity(ControlParameter.Control c,
        SystemControl           sc)  // system control
        => Mathf.InverseLerp(c.values.x, c.values.y, sc.value);

    float Activity(ControlParameter.Control c,
        ExcreterStruct          e)
        => Mathf.InverseLerp(c.values.x, c.values.z, e.dailyRate);

    //eliminators are either on or off. Ignore activity.

    float Activity(ControlParameter.Control c,
        SensorStruct            s)
    {
        switch (c.objective)
        {
            case ControlParameter.Control.Objective.maximise:
                // a low min value means want to trigger more, which means higher activity
                return Mathf.InverseLerp(c.values.y, c.values.x, s.minValue);

            case ControlParameter.Control.Objective.minimise:
                // a low max value means want to trigger more, which means higher activity
                return 1f - Mathf.InverseLerp(c.values.y, c.values.x, s.maxValue);

            default: // a min (or max) value that is close to the default value means we want to trigger more, which means higher activity
                return Mathf.InverseLerp(c.values.x, c.values.z,s.minValue);
        }
    }
    
    private void LearnGeneUtility()
    {
        Debug.Log("Learning gene utility");
        List<ReactorInput> newSet = previousSets[^1];
        int wins = 0;
        int trials = 0;
        for (int r = 0; r < newSet.Count; r++)
        {
            trials++;
            if (newSet[r].parentControls == null && newSet[r].parentSensors == null &&
                newSet[r].parentExcreters == null && newSet[r].parentEliminators == null) //IF HAS NO PARENT DATA
            {
                continue;
            }

            if (newSet[r].score > newSet[r].parentScore || newSet[r].score == newSet[r].parentScore && newSet[r].costResult < newSet[r].parentCostResult) wins++;
            
            int dq = newSet[r].score - newSet[r].parent2Score;
            if (dq == 0) continue;

            ReactorInput newR = newSet[r];
            ReactorInput oldR = CreateComparisonReactor(newSet[r].parentControls,newSet[r].parentSensors, newSet[r].parentExcreters,newSet[r].parentEliminators); //regenerate from parentvalues
            
            // 1) quality-gain bookkeeping
            var changed = DiffIndices(oldR, newR);
            foreach (int g in changed)
            {
                qGainSum[g]    += dq;
                qGainTrials[g] += 1;
            }

            // 2) Δactivity × dq accumulation (for informing differential batches)
            for (int i = 0; i < allControls.Count; i++)
            {
                ControlParameter.Control c = allControls[i];

                float aOld = GetActivity(c, oldR);
                float aNew = GetActivity(c, newR);
                float dA   = aNew - aOld;
                if (Mathf.Abs(dA) < 1e-4f) continue;

                gradSum[i] += dq * dA;
                gradCnt[i] += 1;
            }
            
            //EXAMINING BOTH PARENTS FROM CROSSOVER
            if (newSet[r].parentControls2 != null || newSet[r].parentSensors2 != null ||
                newSet[r].parentExcreters2 != null || newSet[r].parentEliminators2 != null) //IF HAS A PARENT2
            {
                trials++;
                if (newSet[r].score > newSet[r].parent2Score || newSet[r].score == newSet[r].parent2Score && newSet[r].costResult < newSet[r].parent2CostResult) wins++;
                dq = newSet[r].score - newSet[r].parentScore;
                if (dq == 0) continue;

                oldR = CreateComparisonReactor(newSet[r].parentControls2,newSet[r].parentSensors2, newSet[r].parentExcreters2,newSet[r].parentEliminators2); //regenerate from parentvalues

                changed = DiffIndices(oldR, newR);
                foreach (int g in changed)
                {
                    qGainSum[g]    += dq;
                    qGainTrials[g] += 1;
                }

                for (int i = 0; i < allControls.Count; i++)
                {
                    ControlParameter.Control c = allControls[i];

                    float aOld = GetActivity(c, oldR);
                    float aNew = GetActivity(c, newR);
                    float dA   = aNew - aOld;
                    if (Mathf.Abs(dA) < 1e-4f) continue;

                    gradSum[i] += dq * dA;
                    gradCnt[i] += 1;
                }
            }
        }
        crossoverRate = Mathf.Lerp(crossoverRate,Mathf.Clamp(Mathf.Exp( ((float)wins/trials - 0.2f) / 0.4f ),0.1f,0.8f), 0.4f); //rechenburg crossover equation, lerped for smoothness. P_Star = 0.2, Scale = 0.4
        Debug.Log("crossoverRate: " + crossoverRate);
    }
    
    //  Utility: Creates a signature for a ReactorInput
    private static string Fingerprint(ReactorInput r)
    {
        var sb = new System.Text.StringBuilder();

        // 1) System controls  (name:value)
        foreach (var c in r.systemControls.OrderBy(x => x.name))
            sb.Append($"{c.name}:{c.value:F4};");

        // 2) Sensors          (name:min:max)
        foreach (var s in r.sensors.OrderBy(x => x.name))
            sb.Append($"{s.name}:{s.minValue:F4}:{s.maxValue:F4};");

        // 3) Excreters        (name:rate)
        foreach (var e in r.excreters.OrderBy(x => x.name))
            sb.Append($"{e.name}:{e.dailyRate:F4};");

        // 4) Eliminators      (name:on/off)
        foreach (var e in r.eliminators.OrderBy(x => x.name))
            sb.Append($"{e.name};");

        return sb.ToString();
    }

    // handy wrapper
    float GetActivity(ControlParameter.Control c, ReactorInput r)
    {
        switch (c.type)
        {
            case 0:
                var s = r.sensors   .FirstOrDefault(x => x.name == c.name);
                return (s.name == "") ? 0.5f : Activity(c, s);

            case 1:
                var e = r.excreters .FirstOrDefault(x => x.name == c.name);
                return (e.name == "") ? 0.5f : Activity(c, e);

            case 2:
                bool on = r.eliminators.Any(x => x.name == c.name);
                return on ? 1f : 0f;

            default:
                var sc = r.systemControls.FirstOrDefault(x => x.name == c.name);
                return Activity(c, sc);
        }
    }
    
    
    //WHY THIS WORKS IN THEORY: IF WE HAVE A SENSOR THAT MOVES PAST AN OPTIMAL VALUE (BECAUSE RECORDED GRADIENT IS +VE, THEREFORE INSTRUCTED TO INCREASE), THE GRADIENT IS UPDATED WHEN THE RESULTS ARE NEGATIVE OR SMALL. SO AS WE ARRIVE AT OPTIMAL RESULTS, GRADIENTS GO TO ZERO.
    private List<ReactorInput> GenerateDifferentialBatch()
    {
        Debug.Log("Generating differential batch");
        const float STEP = 0.4f;          // 0..1, how far to move towards bound per iter
        const int   K    = 3;             // tweak this many strongest genes per offspring

        List<ReactorInput> batch = new();

        float[] gradient = new float[gradSum.Length];
        for (int i = 0; i < gradSum.Length; i++)
            gradient[i] = (gradCnt[i] > 0) ? gradSum[i] / gradCnt[i] : 0f;
        
        for (int rep = 0; rep < numReactors; rep++)
        {
            ReactorInput parent = bestReactors[rng.Next(bestReactors.Count)];

            // pick K genes with largest absolute gradient
            var topK = Enumerable.Range(0, gradient.Length)
                .OrderByDescending(i => Mathf.Abs(gradient[i])) //ABSOLUTE SUCH THAT NEGATIVE GRADIENTS ARE INFLUENCED TOO
                .Take(K);

            List<Change> changes = new();
            foreach (int i in topK)
            {
                bool inc   = gradient[i] > 0f;   // increase quality when activity increaase
                float mag  = Mathf.Clamp01(Mathf.Abs(gradient[i])) * STEP;

                changes.Add(new Change
                {
                    indexOfControl = i,
                    increase       = inc,
                    factor         = mag
                });
            }

            batch.Add(EnsureUnique(MutateReactor(changes, parent, false),parent,null));
        }

        return batch;
    }
    
    //  ANALYSIS
    private void AnalyseBatch()
    {
        Debug.Log("Analysing batch");
        for (int i = 0; i < previousSets[^1].Count; i++) //WRITE SCORES TO REACTORS
        {
            ReactorInput r = previousSets[^1][i];
            var scored = new ReactorInput(r, resultSets[^1][i],costSets[^1][i]);
            previousSets[^1][i] = scored;
        }
        
        LearnGeneUtility();
        UpdateParetoFront();
        DecayExplorationStats();
        float max = 21f;
        foreach(ReactorInput r in previousSets[batchIndex]) //get the longest duration
        {
            if (r.systemControls.All(x => x.name != "perExperimentDuration")) continue;
            {
                if(r.systemControls.First(x=>x.name == "perExperimentDuration").value < max)
                    max = r.systemControls.First(x=>x.name == "perExperimentDuration").value;
            }
        }
        elapsedDays += max; 
        UIManager.Message("Elapsed days: " + elapsedDays);
        batchIndex++;
    }

    void DecayExplorationStats()
    {
        Debug.Log("Decaying exploration stats");
        for (int i = 0; i < allControls.Count; i++)
        {
            qGainTrials[i] = Mathf.FloorToInt(qGainTrials[i] * DECAY);
            qGainSum[i] *= DECAY;

            // always leave at least one “ghost” trial to avoid divide-by-zero
            if (qGainTrials[i] == 0) qGainTrials[i] = 1;
        }
    }

    private void UpdateParetoFront()
    {
        Debug.Log("Updating Pareto Front");
        var qs = resultSets[^1];
        var cs = costSets[^1];
        for (int i = 0; i < qs.Count; i++)
        {
            int q = qs[i];
            float cost = cs[i];
            bool dominated = false;
            foreach (var p in paretoFront)
                if (p.q >= q && p.c <= cost && (p.q > q || p.c < cost))
                {
                    dominated = true;
                    break;
                }

            if (dominated) continue;
            paretoFront.RemoveAll(p => q >= p.q && cost <= p.c && (q > p.q || cost < p.c));
            paretoFront.Add(new ParetoEntry(q, cost, batchIndex, i));
        }
    }

    // Utility: Persistent‑state helpers
    private void SaveSnapshot(string algorithmType = "LHCGaussian")
    {
        string stateFile = $"{StartMenu.username}_algo_state.dat";
        var st = new AlgorithmState
        {
            previousSets = previousSets,
            resultSets = resultSets,
            costSets = costSets,
            batchIndex = batchIndex,
            elapsedDays = elapsedDays,
            uncertainty = uncertainty,
            qGainSum = qGainSum,
            qGainTrials = qGainTrials,
            gradSum = gradSum,
            gradCnt = gradCnt,
            paretoFront = paretoFront
        };
        SaveManager.SaveInstruction($"{StartMenu.username}_{InstructionFileName}", batchIndex, previousSets[^1], algorithmType);
        Debug.Log("saved instruction, previous states count: " + st.previousSets.Count);
        SaveManager.SaveObject(stateFile, st, StartMenu.password);
    }

    private bool LoadSnapshot()
    {
        string stateFile = $"{StartMenu.username}_algo_state.dat";
        Debug.Log("attempting load snapshot");
        if (!SaveManager.LoadObject(stateFile, StartMenu.password, out AlgorithmState st))
            return false;
        
        Debug.Log("loaded snapshot");
        Debug.Log("previous states count: " + st.previousSets.Count);

        previousSets.Clear(); previousSets.AddRange(st.previousSets);
        resultSets .Clear();  resultSets .AddRange(st.resultSets);
        costSets   .Clear();  costSets   .AddRange(st.costSets);

        batchIndex = st.batchIndex;
        elapsedDays = st.elapsedDays;
        uncertainty = st.uncertainty;
        qGainSum = st.qGainSum;
        qGainTrials = st.qGainTrials;
        gradSum = st.gradSum;
        gradCnt = st.gradCnt;

        paretoFront.Clear();   paretoFront.AddRange(st.paretoFront);

        foreach (var batch in previousSets)
        {
            foreach (var r in batch)
            {
                seenFingerprints.Add(Fingerprint(r));
            }
        }

        return true;
    }

    //  Evolution step helper
    private void ComputeNextBatch()
    {
       
        string algoType;

        if (elapsedDays < 0.2f * maxDuration)
        {
            algoType = "LHCGaussian";
            UIManager.Message("LHC Gaussian batch",false);
            var batch = LHCGaussianReactor(numReactors).ToList();
            previousSets.Add(batch);
        }
        else if (elapsedDays < 0.6f * maxDuration)
        {
            algoType = "Evolutionary";
            UIManager.Message("Evolutionary batch",false);
            ObtainBestReactors(1f - 1.25f * elapsedDays / maxDuration);
            previousSets.Add(GenerateEvolutionaryBatch(numReactors >= 20 ? crossoverRate : 0f));
        }
        else
        {
            algoType = "Gradient";
            UIManager.Message("Gradient batch",false);
            ObtainBestReactors(0.3f);
            previousSets.Add(GenerateDifferentialBatch());
        }

        SaveSnapshot(algoType);      // save with the right tag
    }

    //  Public API for entering batch results
    public void SubmitBatchResults(List<float> entries)
    {
        int expected = previousSets[batchIndex].Count * 2;
        if (entries == null || entries.Count != expected)
        {
            Debug.LogError($"SubmitBatchResults expected {expected} values (result/cost pairs) but received {entries?.Count ?? 0}");
            return;
        }

        var results = new List<int>();
        var costs   = new List<float>();

        for (int i = 0; i < entries.Count; i += 2)
        {
            results.Add(Mathf.RoundToInt(entries[i]));
            costs.Add(entries[i + 1]);
        }
        
        UIManager.Message("Best result " + results.Max(),false);

        resultSets.Add(results);
        costSets.Add(costs);

        AnalyseBatch();        // updates batchIndex, elapsedDays, pareto front etc...
        ComputeNextBatch();    // produces and saves the new batch
       
        if(elapsedDays > maxDuration)
        {
            CreateParetoResultsTxt();
            Simulation.i.EndSimulation();
            UIManager.Message("Finished experiment!");
        }
        else
        {
            Simulation.i.Go();
        }
    }
    
    public void CreateParetoResultsTxt()
    {
        string dateStamp = DateTime.Now.ToString("yyyy-MM-dd");
        string safeUser = string.IsNullOrEmpty(StartMenu.username) ? "User" : StartMenu.username;
        string fileName = $"{safeUser}_ParetoResults_{dateStamp}.txt";
        string path = System.IO.Path.Combine(Application.dataPath, fileName);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Username: {safeUser}");
        sb.AppendLine($"Date: {dateStamp}");
        sb.AppendLine();
        sb.AppendLine("Pareto Front Results (Cheapest reactors per cartilage score)");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        // Group Pareto entries by score and find the cheapest cost for each
        var grouped = paretoFront
            .GroupBy(p => p.q)
            .Select(g => g.OrderBy(p => p.c).First())
            .OrderByDescending(p => p.q);

        foreach (var entry in grouped)
        {
            var reactor = previousSets[entry.b][entry.i];
            sb.AppendLine($"Cartilage Score: {entry.q}");
            sb.AppendLine($"Total Cost : {entry.c:F3}");
            sb.AppendLine("Controls:");
            foreach (var control in reactor.systemControls.OrderBy(c => c.name))
            {
                sb.AppendLine($"System Control: {control.name}: {control.value:F3}");
            }
            foreach (var sensor in reactor.sensors.OrderBy(s => s.name))
            {
                sb.AppendLine($"Sensor: {sensor.name}: [{sensor.minValue:F3}, {sensor.maxValue:F3}]");
            }
            foreach (var excreter in reactor.excreters.OrderBy(e => e.name))
            {
                sb.AppendLine($"Excreter: {excreter.name}: {excreter.dailyRate:F3}");
            }
            foreach (var eliminator in reactor.eliminators.OrderBy(e => e.name))
            {
                sb.AppendLine($"Eliminator: {eliminator.name}: ON");
            }
            sb.AppendLine(new string('-', 40));
        }

        System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
        Debug.Log("Pareto results written to " + path);
        UIManager.Message("Pareto results saved to " + path, false);
    }
    
    IEnumerable<ReactorInput> LHCGaussianReactor(int n)
    {
        Debug.Log("Generating LHC Gaussian reactor");
        if (n <= 0) yield break;
        int d = allControls.Count;

        // 1)  Build the Latin-Hypercube matrix (size n × d)
        double[,] u = new double[n, d];

        for (int col = 0; col < d; col++)
        {
            // one random point in every stratum
            var strata = Enumerable.Range(0, n)
                                   .Select(i => (i + rng.NextDouble()) / n)
                                   .OrderBy(_ => rng.Next())          // shuffle strata
                                   .ToArray();

            for (int row = 0; row < n; row++)
                u[row, col] = strata[row];
        }

        // 2)  Map U[0,1] →   N(0,1) via probit,  then left-truncate at 0 and squash into approx. [0,1] so that values stay inside range.
        for (int row = 0; row < n; row++)
        for (int col = 0; col < d; col++)
        {
            double z = Probit.Inverse(u[row, col]);   // Φ⁻¹(p)
            switch (z)
            {
                case < -3.0:
                    z = -3.0;                  //clamp
                    break;
                case > 3.0:
                    z = 3.0;
                    break;
            }
            u[row, col] = 0.5 + 0.5 * z / 3.0;    // light squash [0.5 - 1]
        }

        // 3)  Translate each row into a Change-list and yield a ReactorInput
        ReactorInput r = Gen1Reactor();
        for (int row = 0; row < n; row++)
        {
            var changes = new List<Change>();
            for (int col = 0; col < d; col++)
            {
                changes.Add(new Change
                {
                    indexOfControl = col,
                    factor         = (float)u[row,col], //Gaussian value
                    increase       = u[row, col] > 0.5   // direction flag
                   
                });
            }
            yield return EnsureUnique(MutateReactor(changes, r, true),null,null);
        }
    }
    
    //  Select the best reactors from the Pareto front. Primary key quality (higher is better), Secondary key cost (lower is better)
    private void ObtainBestReactors(float percentTop)
    {
        if (paretoFront.Count == 0) { bestReactors.Clear(); return; }

        var ranked = paretoFront
            .OrderByDescending(p => p.q)   // 1) highest quality
            .ThenBy        (p => p.c)      // 2) lowest cost among equals
            .Take(Mathf.Max(1,             // never return an empty list
                Mathf.CeilToInt(paretoFront.Count * percentTop)))
            .ToList();

        bestReactors = ranked
            .Select(p => previousSets[p.b][p.i])
            .ToList();
    }
    bool ReachedStoppingCondition() //INCOMPLETE
    {
        return elapsedDays > maxDuration;
    }

    float PredictBaseCost(ReactorInput r) //THIS CAN BE IGNORED, THE ACTUAL COSTS WILL BE GIVEN IN EACH ITERATION
    {
        (float i, float c) = GetCostsFromReactor(r);
        return i + c * 21f;
    }
}