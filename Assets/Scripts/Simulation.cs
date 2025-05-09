using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Simulation : MonoBehaviour
{
    public static Simulation i;
    public static float timeScale = 3600f;
    public static float timer = 0f;
    public Bioreactor prefab;
    public GrowthEquation equation; //Determines how the cartilage reactor grows

    public List<Bioreactor> reactors = new List<Bioreactor>();

    private bool simulating;
    public Slider slid;

    private void Awake()
    {
        i = this;
    }

    public void Init()
    {
        for (int i = 0; i < MasterAlgorithm.numReactors; i++)
        {
            reactors.Add(Instantiate(prefab, new Vector3(i*2.5f, 0, 0), Quaternion.identity, null ));
        }
        slid.gameObject.SetActive(true);
        slid.onValueChanged.AddListener(val => timeScale = val * 3600f);
        Go();
    }

    public void Go()
    {
        //Result,Cost,Result,Cost //numBioreactors x 2
        int i = 0;
        foreach(MasterAlgorithm.ReactorInput r in MasterAlgorithm.i.previousSets[^1])
        {
            reactors[i].Init(r);
            i++;
        }
        timer = 0f;
        simulating = true;
        Debug.Log("Go!!!");
    }

    public void PauseAndReset()
    {
        simulating = false;
        timer = 0f;
        foreach (Bioreactor b in reactors)
        {
            b.Clear();
        }
    }

    public void Update()
    {
        if (simulating)
        {
            timer += Time.deltaTime * timeScale;
            foreach(Bioreactor b in reactors)
            {
                if (b.finished)
                {
                    SubmitBatch();
                }
            }
        }
    }

    public void SubmitBatch()
    {
        List<float> results = new();
        foreach (Bioreactor b in reactors)
        {
            results.Add(equation.Assess(b.averageVariables.Select(x => x / b.frameCount).ToArray(), b.GetMech()));
            results.Add(b.GetCost());
        }
        PauseAndReset();
        for (int r = 0; r < reactors.Count; r++)
        {
            var inp = reactors[r].settings;
            Debug.Log(
                $"Reac {r:D2}  Score={results[r*2]:F3}  Cost={results[r*2+1]:F0}\n" +
                string.Join(", ",
                    inp.systemControls.Select(s => $"{s.name}:{s.value:F1}")
                        .Concat(inp.sensors   .Select(s => $"{s.name}[{s.minValue:F1},{s.maxValue:F1}]"))
                        .Concat(inp.excreters .Select(e => $"{e.name}:{e.dailyRate:F1}"))
                        .Concat(inp.eliminators.Select(e => $"{e.name}:ON"))));
        }
        MasterAlgorithm.i.SubmitBatchResults(results);
    }

    public void EndSimulation()
    {
        PauseAndReset();
    }
}

