using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class SetupExperiment : MonoBehaviour
{
    [SerializeField] TMP_InputField[] ifs; //Num Bio-reactors, Manufacture Cost, Maintenance Cost,  Max Duration, Serum Price, System Fluid Volume
    [SerializeField] private Button[] updown;
    [SerializeField] private ControlParameter prefab;
    [SerializeField] private List<ControlParameter> completedParams;
    private ControlParameter current;
    [SerializeField] private RectTransform parent;
    [SerializeField] private TextMeshProUGUI[] txts;
    [SerializeField] private ControlSO[] sensors;
    [SerializeField] private ControlSO[] excreters;
    [SerializeField] private ControlSO[] eliminators;
    [SerializeField] private ControlSO[] systemControls;
    public static SetupExperiment i;

    [SerializeField] private Button saveConfigButton;  

    private void Awake()
    {
        i = this;
        StartMenu.newExperiment = true;

        ControlParameter.sos = new List<List<ControlSO>>();
        ControlSO[][] src = { sensors, excreters, eliminators, systemControls };

        //clone all the scriptable objects so no changes are made to the originals
        foreach (var arr in src)
        {
            var list = new List<ControlSO>(arr.Length);
            foreach (var so in arr)
            {
                var clone = Instantiate(so);
                clone.name = so.name;
                list.Add(clone);
            }
            ControlParameter.sos.Add(list);
        }
        completedParams = new List<ControlParameter>();
    }
    
    
    public void AddControl(ControlParameter cp)
    {
        completedParams.Add(cp);
        RefreshStats();
        current = Instantiate(prefab,parent);
    }
    
    public void RemoveControl(ControlParameter cp)
    {
        completedParams.Remove(cp);
        RefreshStats();
        if(current != cp) Destroy(current.gameObject);
        current = cp;
    }

    void RefreshStats()
    {
        List<ControlParameter.Control> cs = new List<ControlParameter.Control>();
        foreach(ControlParameter cp in completedParams)
        {
            cs.Add(cp.GetControl());
        }
        
        //TRIALS
       
      
        
        //STARTUP COSTS
        float initCost = cs.Sum(x => x.startupCost);
        int numBioReactors = 1;
        float manufactureCost = 1500f;
        if (float.TryParse(ifs[0].text, out var num) && float.TryParse(ifs[1].text, out var manCost))
        {
            numBioReactors = (int)num;
            manufactureCost = manCost;
            initCost += manufactureCost * numBioReactors;
            if(float.TryParse(ifs[3].text, out float maxDur))
            {
                float minDays = 21f;
                if (cs.Any(x => x.type == 18))
                {
                    minDays = cs.First(x => x.type == 18).values.x;
                }
                int maxTrials = Mathf.CeilToInt(num * maxDur / minDays);
                txts[0].text = "Max Trials: " + maxTrials;
            }
        }
        
        //CONTINUATION COSTS
        txts[1].text = $"Production Costs: £{initCost:N2}";
        float continuationCost = cs.Sum(c => c.continuationCost);
        
        float synovialFluidCostPerLitre = 100f; //£
        if (float.TryParse(ifs[4].text, out var cos))
        {
            synovialFluidCostPerLitre = cos;
        }
        float fluidVolume = 250; //ml
        if (float.TryParse(ifs[4].text, out var fl))
        {
            fluidVolume = fl;
        }
       
        float replenishCostsMin = 0f; //weekly
        float replenishCostsMax = 0f; //weekly
        float mediaExchangeMinAmount = 50f; //percent
        float mediaExchangeMaxAmount = 50f; //percent
        float mediaExchangeCyclesMin = 1440f; //minutes
        float mediaExchangeCyclesMax= 1440f; //minutes
        
        foreach (ControlParameter.Control c in cs) //ADDITIONAL VARIABLE COSTS:
        {
            switch (c.type)
            {
                case 1:  //Add extra costs based on the fluid cost and the maximum amount.
                     replenishCostsMin += c.values.x * c.supplyCost * 7f;
                     replenishCostsMax += c.values.z * c.supplyCost * 7f; //z for second input (only 2)
                    break;
                case 4: //media exchange percentage effects media exchange regularity cost
                    mediaExchangeMinAmount = c.values.x;
                    mediaExchangeMaxAmount = c.values.y;
                    break;
                case 5: //Media exchange min and max cycles per day utilised.
                    mediaExchangeCyclesMin = c.values.x;
                    mediaExchangeCyclesMax = c.values.z; //z for second input (only 2)
                    break;
                default: //Sensors, eccentricity, perfusion, temperature, RPM Modes have no variable costs for simplicity.
                    break;
            }
        }
        
        //Conversions: mediaExchangeAmounts in percent, 10080 minutes in a week, 1000ml in a litre
        float minContCost = continuationCost + replenishCostsMin +
                            mediaExchangeMinAmount/100f * 10080f/mediaExchangeCyclesMax * synovialFluidCostPerLitre * fluidVolume/1000f;
        float maxContCost = continuationCost + replenishCostsMax +
                            mediaExchangeMaxAmount/100f * 10080f/mediaExchangeCyclesMin * synovialFluidCostPerLitre * fluidVolume/1000f;
        
        minContCost *= numBioReactors; //times continuationCosts by number of reactors
        maxContCost *= numBioReactors;
        
        if (float.TryParse(ifs[2].text, out var opCost)) //Add the total weekly operational costs
        {
            minContCost += opCost;
            maxContCost += opCost;
        }
        
        txts[2].text = $"Est. Weekly Continuation Costs: £{minContCost:N2} - £{maxContCost:N2}";
        if (float.TryParse(ifs[3].text, out var t))
        {
            float min = initCost + minContCost * t/7f;
            float max = initCost + maxContCost * t/7f;
            txts[3].text = $"Est. Total Cost: £{min:N2} - £{max:N2}";
        }
    }
    
    private void Start()
    {
        foreach (TMP_InputField i in ifs)
        {
            i.text = "";
            i.onDeselect = new TMP_InputField.SelectionEvent();
            i.onDeselect.AddListener(_ => CheckInputs());
        }

        updown[0].onClick.AddListener(Up);
        updown[1].onClick.AddListener(Down);

        if (saveConfigButton != null) saveConfigButton.onClick.AddListener(SaveConfiguration);
    }
    
    private void SaveConfiguration()
    {
        if (!AreGlobalsValid(out var gl))
        {
            UIManager.Message("All global controls must be filled with positive numbers before you can save.");
            return;
        }

        var payload = new List<ControlParameter.Control>();
        foreach (var cp in completedParams)
            payload.Add(cp.GetControl());

        if (payload.Count == 0)
        {
            UIManager.Message("Add at least one control parameter before saving.");
            return;
        }

        try
        {
            SaveManager.Save(payload,gl, StartMenu.password);
            UIManager.Message("Configuration saved!", false);
            saveConfigButton.gameObject.SetActive(false);
            LeanTween.delayedCall(3f, () =>
            {
                SceneManager.LoadScene(2);
            });
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            UIManager.Message("Failed to save configuration.");
        }
    }
    
    private bool AreGlobalsValid(out float [] globals)
    {
        globals = new float[ifs.Length];
        for (int i = 0; i < ifs.Length; i++)
        {
            if (!float.TryParse(ifs[i].text, out var v) || v <= 0f)
                return false;
            globals[i] = v;
        }
        return true;
    }
    
    void Up()
    {
        if (CheckNumChildren() == parent.childCount) return;
        //Find the last deactivated child and activate it
        Transform x = null;
        foreach (Transform t in parent)
        {
            if (!t.gameObject.activeInHierarchy)
            {
                x = t;
            }
        }
        x?.gameObject.SetActive(true);
    }
    
    void Down()
    {
        if (CheckNumChildren() <= 6) return;
        foreach (Transform t in parent)
        {
            if (t.gameObject.activeInHierarchy)
            {
                t.gameObject.SetActive(false);
                return;
            }
        }
    }

    int CheckNumChildren()
    {
        int n = 0;
        foreach (Transform t in parent)
        {
            if(t.gameObject.activeInHierarchy)
            {
                n++;
            }
        }
        return n;
    }

    void CheckInputs()
    {
        bool allFilled = true;
        foreach (TMP_InputField i in ifs)
        {
            if(! i.gameObject.activeInHierarchy) continue;
            if (i.text == "")
            {
                allFilled = false;
                break;
            }
            else
            {
                if (float.TryParse(i.text, out float val))
                {
                    if (val <= 0)
                    {
                        allFilled = false;
                        break;
                    }
                }
              
            }
        }
        ControlParameter.canOpen = allFilled;
    }
    
}
