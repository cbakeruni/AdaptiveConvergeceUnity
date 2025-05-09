using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Bioreactor : MonoBehaviour
{
    [Header("Prefabs / Assets")]
    [SerializeField] private Serum inReactorSerum;
    [SerializeField] private Serum outReactorSerum;
    [SerializeField] private Serum vat;
    [SerializeField] private Motor motor;
    [SerializeField] private Sensor sensorPrefab;
    [SerializeField] private Excreter excreterPrefab;
    [SerializeField] private Eliminator eliminatorPrefab;

    [Header("Attachment Points")]
    [SerializeField] private List<Transform> sensorPositions = new ();
    [SerializeField] private List<Transform> excretorPositions = new ();
    [SerializeField] private List<Transform> eliminatorPositions = new ();

    public MasterAlgorithm.ReactorInput settings;
    public bool finished;
    
    public  float initCosts;
    public  float contConsts;
    public float extraCosts;
    
    private readonly List<Sensor> sensors = new ();
    private readonly List<Excreter> excreters  = new ();
    private readonly List<Eliminator> eliminators = new ();
    private readonly List<ScheduledEvent> scheduledEvents = new ();

    public float[] averageVariables;
    public int frameCount;
    public float mechScore = 0f;
    private float lastMediaExchangeTime = -Mathf.Infinity;
    private bool  mediaExchangeFlag;

    
    //SIM CONSTANTS (default fall-backs may get overwritten in Init)
    public float mediaExchangePercent =  0.50f;     // fraction of total vol
    public float mediaExchangeRegularity  = 86_640f;    // sec
    public float perfusion =  0.000278f; // um per hour -> ml s-1
    public float temperature = 37f;
    public float initEccentricity = 0.7f;
    public float finalEccentricity = 0.9f;
    public float initRPM = 60f;
    public float finalRPM = 120f;
    public float initMotorOnDuration = 900f;
    public float finalMotorOnDuration = 2700f;
    public float initMotorOffDuration = 2700f;
    public float finalMotorOffDuration = 900f;
    public float dailyRestPeriod = 28_880f;
    public float initStaticPeriod = 5_760f; //hours -> seconds
    public float perExperimentDuration = 1_814_400f; // 21 days -> seconds
    
    public void Init(MasterAlgorithm.ReactorInput r)
    {
        // Wipe any previous state first
        Clear();
        averageVariables = new float[inReactorSerum.variables.Count];
        frameCount = 0;
        settings = r;

        // system controls
        foreach (var sc in r.systemControls)
        {
            switch (sc.name.ToLower())
            {
                case "mediaexchangepercent": mediaExchangePercent = sc.value / 100f;  break;
                case "mediaexchangeregularity":   mediaExchangeRegularity = sc.value * 60f;   break; // mins→sec
                case "perfusion": perfusion = sc.value / 36000000; break; // μl h-1 → ml s-1
                case "temperature": temperature = sc.value; break;
                case "initeccentricity": initEccentricity = sc.value; break;
                case "finaleccentricity": finalEccentricity = sc.value; break;
                case "initrpm": initRPM = sc.value; break;
                case "finalrpm": finalRPM = sc.value; break;
                case "initmotoronduration": initMotorOnDuration = sc.value * 60f;   break;
                case "finalmotoronduration": finalMotorOnDuration = sc.value * 60f;   break;
                case "initmotoroffduration": initMotorOffDuration = sc.value * 60f;   break;
                case "finalmotoroffduration": finalMotorOffDuration = sc.value * 60f;   break;
                case "dailyrestperiod": dailyRestPeriod = sc.value * 60f;   break;
                case "initstaticperiod": initStaticPeriod = sc.value * 60f;   break;
                case "perexperimentduration": perExperimentDuration= sc.value * 86400f; break; // days→sec
            }
        }

        // sensors
        for (int i = 0; i < r.sensors.Count; i++)
        {
            var sIn  = r.sensors[i];
            var pos  = sensorPositions[Mathf.Clamp(i, 0, sensorPositions.Count - 1)];
            var s    = Instantiate(sensorPrefab, pos.position, pos.rotation, transform);
            s.name = sIn.name;
            s.variable = sIn.name;
            s.range = new Vector2(sIn.minValue, sIn.maxValue);
            s.serum = outReactorSerum;
            sensors.Add(s);
        }

        // excreters
        for (int i = 0; i < r.excreters.Count; i++)
        {
            var eIn  = r.excreters[i];
            var pos  = excretorPositions[Mathf.Clamp(i, 0, excretorPositions.Count - 1)];
            var e    = Instantiate(excreterPrefab, pos.position, pos.rotation, transform);
            e.name = eIn.name;
            e.variable = eIn.name;
            if (eIn.hasSensor)
            {
                e.maxDailyRate = eIn.dailyRate;
                e.minDailyRate = 0f;
            }
            else
            {
                e.minDailyRate = eIn.dailyRate;
                e.maxDailyRate = eIn.dailyRate;
            }
            e.serum = outReactorSerum;
            e.reactor = this;
            excreters.Add(e);
        }

        // eliminators
        for (int i = 0; i < r.eliminators.Count; i++)
        {
            var elIn = r.eliminators[i];
            var pos  = eliminatorPositions[Mathf.Clamp(i, 0, eliminatorPositions.Count - 1)];
            var el   = Instantiate(eliminatorPrefab, pos.position, pos.rotation, transform);
            el.name  = elIn.name;
            el.serum = outReactorSerum;
            eliminators.Add(el);
        }

        // cost baselines
        initCosts  = r.initCost + MasterAlgorithm.manufactureCost;
        contConsts = r.contCost;

        //Environment
        inReactorSerum .temperature = temperature;
        outReactorSerum.temperature = temperature;
        vat             .temperature = temperature;
        inReactorSerum.ml = 2;
        outReactorSerum.ml = MasterAlgorithm.systemFluidVolume;
        vat.ml = Mathf.Infinity;
        mechScore = 0f;

        //Generate motor timeline
        BuildMotorSchedule();
        
        finished = false;
        extraCosts = 0f;
    }

    public void Clear()   // fully reset
    {
        // Destroy hardware
        sensors    .ForEach(s => Destroy(s.gameObject));
        excreters  .ForEach(e => Destroy(e.gameObject));
        eliminators.ForEach(e => Destroy(e.gameObject));
        sensors.Clear(); excreters.Clear(); eliminators.Clear();

        // fluids
        inReactorSerum .Reset();
        outReactorSerum.Reset();
        vat            .Reset();

        // motor
        motor.RPM = 0f;
        motor.eccentricity = 0f;
        motor.logs.Clear();

        // flags & timers
        scheduledEvents.Clear();
        lastMediaExchangeTime = -Mathf.Infinity;
        mediaExchangeFlag = false;
        extraCosts       = 0f;
        mechScore        = 0f;
        finished         = false;
    }

    private void BuildMotorSchedule()
    {
        float t = initStaticPeriod;            // initial “rest” phase
        inReactorSerum.temperature  = temperature;
        outReactorSerum.temperature = temperature;
        vat.temperature             = temperature;

        while (t < perExperimentDuration)
        {
            // daily maintenance window
            float daily = 0f;
            daily += dailyRestPeriod;

            while (daily < 86_400f)            // seconds in a day
            {
                float now = t + daily;

                // interpolate progression of parameters across full study
                float lerp = now / perExperimentDuration;
                float ecc  = Mathf.Lerp(initEccentricity, finalEccentricity, lerp);
                float rpm  = Mathf.Lerp(initRPM,          finalRPM,          lerp);
                float on   = Mathf.Lerp(initMotorOnDuration,  finalMotorOnDuration,  lerp);
                float off  = Mathf.Lerp(initMotorOffDuration, finalMotorOffDuration, lerp);

                scheduledEvents.Add(new ScheduledEvent(() => motor.SetOn(rpm, ecc, on),  now));
                daily += on;
                scheduledEvents.Add(new ScheduledEvent(() => motor.SetOn(0f, 0f, off),   now + on));
                daily += off;
            }
            t += daily;
        }

        // first media-exchange timer
        scheduledEvents.Add(new ScheduledEvent(TriggerMediaExchange, mediaExchangeRegularity));
    }


    private void Update()
    {
        // Time-ordered execution
        for (int i = scheduledEvents.Count - 1; i >= 0; i--)
        {
            if (scheduledEvents[i].time > Simulation.timer) continue;
            scheduledEvents[i].act.Invoke();
            scheduledEvents.RemoveAt(i);
        }

        if (mediaExchangeFlag &&
            Simulation.timer - lastMediaExchangeTime > mediaExchangeRegularity)
        {
            TriggerMediaExchange();
        }

        UpdateVariables();

        DoPerfusion();

        // mark completion
        if (!finished && Simulation.timer >= perExperimentDuration)
        {
            finished = true;
        }
    }

    private void UpdateVariables()
    {
        // Increment frame count for averaging later
        frameCount++;

        // Convert keys to list for indexed access
        var keys = new List<string>(inReactorSerum.variables.Keys);

        for (int i = 0; i < keys.Count; i++)
        {
            // Add the current value to the running total
            averageVariables[i] += inReactorSerum.variables[keys[i]];
        }
    }

    private void DoPerfusion()
    {
        float xfer = Mathf.Min(outReactorSerum.ml - 1,
                               perfusion * Time.deltaTime * Simulation.timeScale);
        outReactorSerum.MixInto(inReactorSerum, xfer);
        inReactorSerum.MixInto(outReactorSerum, xfer);
    }

    public void TriggerMediaExchange()
    {
        if (Simulation.timer - lastMediaExchangeTime < mediaExchangeRegularity)
        {
            mediaExchangeFlag = true;
            return;
        }
        mediaExchangeFlag = false;

        float vol = mediaExchangePercent * MasterAlgorithm.systemFluidVolume;
        vat.MixInto(outReactorSerum, vol);
        outReactorSerum.ml -= vol;

        extraCosts += vol * MasterAlgorithm.serumPrice / 1000f;
        lastMediaExchangeTime = Simulation.timer;

        // schedule next automatic exchange
        scheduledEvents.Add(new ScheduledEvent(TriggerMediaExchange,
            Simulation.timer + mediaExchangeRegularity));
        
    }
    
    // RESULTS & COSTS
    public float GetCost()
        => extraCosts + initCosts + contConsts * perExperimentDuration / 604_800f; // weeks
    
    private static float Normalise(float v, float scale) => Mathf.Clamp01(v / scale);
    
    public struct ScheduledEvent
    {
        public Action act;
        public float  time;
        public ScheduledEvent(Action a, float t) { act = a; time = t; }
    }

    public float[] GetMech()
    {
        return new float[]{perfusion,temperature,
            initEccentricity,finalEccentricity,
            initRPM,finalRPM,
            initMotorOnDuration,finalMotorOnDuration,
            initMotorOffDuration,finalMotorOffDuration,
            dailyRestPeriod,initStaticPeriod};
        
    }
}
