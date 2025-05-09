using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Object = UnityEngine.Object;

public class ControlParameter : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private GameObject[] groups; //0: Basic, 1: Select Type, 2: Select Individual, 3:Sensor/Excreter/Eliminator, 4: Motor, 5: Pump. 6: Valve
    [SerializeField] private Button plusMinus;
    [SerializeField] private TextMeshProUGUI plusMinusText;
    [SerializeField] private Button[] group1Buttons;
    [SerializeField] private Button[] group2Buttons;
    [SerializeField] private Button[] group3Buttons;
    [SerializeField] private TextMeshProUGUI[] group3Texts; 
    [SerializeField] private GameObject currentGroup; 
    public TMP_InputField[] group3Inputs;
    [SerializeField] private SliderGroup slid;

    [SerializeField] public static List<List<ControlSO>> sos;
    [SerializeField] private TextMeshProUGUI selectText;
    [SerializeField] private Button optionPrefab;
    [SerializeField] List<RectTransform> options = new List<RectTransform>();
    [SerializeField] private ControlSO currentSO;
    [SerializeField] private HorizontalLayoutGroup hz;
    [SerializeField] private Transform offs;
    public static bool canOpen = false;
    private int typInd;
    public float manufacture = 0f;
    public float maintenance = 0f;
    
    private void Start()
    {
        currentGroup = null;
        OpenSelection(0);
        //Left button for horizontal layout group
        //Left is always position 0.
        //Right is at max position 6.
        //Left moves all the indexes left by 1
        //Right moves all the indexes right by 1. Uses a for loop to move all the indexes (childPosition)
        //There exist additional children who are deactivated
        group2Buttons[0].onClick.AddListener(() => //move left (insert at right)
        {
            if(sos[typInd].Count < 7) return;
            var o = hz.transform.GetChild(1);
            o.transform.parent = offs;
            o.gameObject.SetActive(false);
            var n = offs.GetChild(0);
            n.transform.parent = hz.transform;
            n.transform.SetSiblingIndex(hz.transform.childCount-2);
            n.gameObject.SetActive(true);
            
        });
        group2Buttons[1].onClick.AddListener(() => //move right (insert at left)
        {
            if(sos[typInd].Count < 7) return;
            var o = hz.transform.GetChild(6);
            o.transform.parent = offs;
            o.transform.SetSiblingIndex(0);
            o.gameObject.SetActive(false);
            var n = offs.GetChild(offs.childCount-1);
            n.transform.parent = hz.transform;
            n.transform.SetSiblingIndex(1);
            n.gameObject.SetActive(true);
        });
    }
    
    public void OpenSelection(int select)
    {
        if (currentGroup == groups[select])
        {
            Debug.LogWarning("Already in this group");
            return;
        }
        currentGroup?.SetActive(false);
        groups[select].SetActive(true);
        currentGroup = groups[select];
        switch (select)
        {
            case 0: //0 Just Plus Button
                plusMinusText.text = "+";
                plusMinus.onClick.RemoveAllListeners();
                plusMinus.onClick.AddListener(() =>
                {
                    if (canOpen)
                    {
                        OpenSelection(1);
                    }
                    else
                    {
                        UIManager.Message("All the above global parameters must be defined and positive before adding a control variable");
                    }
                });
                break;
            case 1: //1 Minus Button, Press System Control / Sensor / Excreter / Eliminator 
                plusMinusText.text = "-";
                plusMinus.onClick.RemoveAllListeners();
                plusMinus.onClick.AddListener(() => OpenSelection(0));
                for (int i = 0; i < group1Buttons.Length; i++)
                {
                    var i1 = i;
                    group1Buttons[i].onClick.RemoveAllListeners();
                    group1Buttons[i].onClick.AddListener(() =>
                    {
                        typInd = i1;
                        Debug.Log(typInd);
                        OpenSelection(2);
                    });
                }
                break;
            case 2: //Setup selection buttons for each control
                for (int y = 0; y < options.Count; y++) 
                {
                    Destroy(options[y].gameObject);
                    options.RemoveAt(y);
                    y--;
                }

                SetSelectText(typInd);
                
                int x = 0;
                for (int z = 0; z < sos[typInd].Count; z++) 
                {
                    var b = Instantiate(optionPrefab,groups[2].transform);
                    options.Add(b.GetComponent<RectTransform>());
                    b.GetComponentInChildren<TextMeshProUGUI>().text = sos[typInd][z].c.name;
                    b.GetComponentInChildren<Image>().color = ColorFromTyp(typInd) * 1.5f;
                    var cur = sos[typInd][z];
                    b.onClick.AddListener(() => currentSO = cur);
                    b.onClick.AddListener(() => OpenSelection(3));
                    if (x > 5)
                    {
                        b.transform.SetParent(offs);
                        b.gameObject.SetActive(false);
                    }
                    else
                    {
                        b.transform.parent = hz.transform;
                        b.transform.SetSiblingIndex(hz.transform.childCount-2);
                    }
                    x++; 
                }
                break;
            case 3: //3 Name, ABC, Variation, Finanical Range, Confirm
                slid.gameObject.SetActive(false);
                ResetGroup3();
                group3Texts[3].text = currentSO.c.name;
                group3Texts[4].text = "£" + currentSO.c.startupCost.ToString("F1") + " | £" + currentSO.c.continuationCost.ToString("F1") + " / week";
                foreach (TMP_InputField i in group3Inputs)
                {
                    i.text = "";
                    i.onDeselect.RemoveAllListeners();
                    i.onDeselect.AddListener(ctx => CheckInputs());
                }
                typInd = currentSO.c.type; //set the typInd again so that system controls may be implemented.
                group3Texts[4].gameObject.SetActive(typInd<3); //turn off costs for system controls
                switch (typInd)
                {
                    case 0: //sensor writes Min(ml/L), Max (ml/L), Default (ml/L)
                        group3Texts[0].text = "Min <size=50%>" + currentSO.c.Measure();
                        group3Texts[1].text = "Max <size=50%>" + currentSO.c.Measure();
                        group3Texts[2].text = "Default <size=50%>" + currentSO.c.Measure();
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToStandard();
                        break;
                    case 1: //excreter writes Max mg per week (mg/week).
                        group3Texts[0].text = "Min <size=50%>" + currentSO.c.Measure() + " / day";
                        group3Texts[2].text = "Max <size=50%>" + currentSO.c.Measure() + " / day";
                        group3Texts[4].text = "£" + currentSO.c.startupCost.ToString("F1") + " | £" + currentSO.c.supplyCost.ToString("F1") + " / " + currentSO.c.Measure();
                        group3Texts[1].text = "";
                        group3Inputs[0].placeholder.GetComponent<TextMeshProUGUI>().text = currentSO.c.values.x.ToString();
                        group3Inputs[2].placeholder.GetComponent<TextMeshProUGUI>().text = currentSO.c.values.y.ToString();
                        group3Inputs[1].gameObject.SetActive(false);
                        SetInputsToStandard();
                        break;
                    case 2: //eliminator NOTHING WRITTEN, IS PASSIVE
                        group3Texts[0].text = "";
                        group3Texts[2].text = "";
                        group3Texts[1].text = "";
                        group3Inputs[0].gameObject.SetActive(false);
                        group3Inputs[1].gameObject.SetActive(false);
                        group3Inputs[2].gameObject.SetActive(false);
                        SetInputsToStandard();
                        break;
                    case 3:
                        throw new Exception("TYPIND ASSIGN ERROR: " + currentSO.name);
                    case 4: //5 Media Exchange Percent
                        group3Texts[0].text = "<size=50%> Min Percent";
                        group3Texts[1].text = "<size=50%> Max Percent" ;
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToMediaExchange();
                        break;
                    case 5: //5 Media Exchange Regularity
                        group3Texts[0].text = "<size=50%> Min Minutes Between Cycles";
                        group3Texts[2].text = "<size=50%> Max Minutes Between Cycles";
                        group3Texts[1].text = "";
                        group3Inputs[0].placeholder.GetComponent<TextMeshProUGUI>().text = currentSO.c.values.x.ToString();
                        group3Inputs[2].placeholder.GetComponent<TextMeshProUGUI>().text = currentSO.c.values.y.ToString();
                        group3Inputs[1].gameObject.SetActive(false);
                        SetInputsToStandard();
                        break;
                    case 6: // Perfusion
                        group3Texts[0].text = "<size=50%> Min uL Per Hour";
                        group3Texts[1].text = "<size=50%> Max uL Per Hour";
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToStandard();
                        break;
                    case 7: // Temperature
                        group3Texts[0].text = "<size=50%> Min Degrees Celsius";
                        group3Texts[1].text = "<size=50%> Max Degrees Celsius";
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToStandard(); 
                        break;
                    case 8 or 9: // Eccentricity Init/End
                        group3Texts[0].text = "<size=50%> Min Eccentricity (0.5 - 1)";
                        group3Texts[1].text = "<size=50%> Max Eccentricity (0.5 - 1)";
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToEccentricity();
                        break;
                    case 10 or 11: //Motor Init/Final RPM
                        group3Texts[0].text = "<size=50%> Min RPM";
                        group3Texts[1].text = "<size=50%> Max RPM";
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToStandard();
                        break;
                    case 12 or 13: //Motor Init/Final On Duration
                        group3Texts[0].text = "<size=50%> Min On Duration (Minutes)";
                        group3Texts[1].text = "<size=50%> Max On Duration (Minutes)";
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToStandard();
                        break;
                    case 14 or 15: //Motor Init/Final Off Duration 
                        group3Texts[0].text = "<size=50%> Min Off Duration (Minutes)";
                        group3Texts[1].text = "<size=50%> Max Off Duration (Minutes)";
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToStandard();
                        break;
                    case 16: //Daily Rest Period
                        group3Texts[0].text = "<size=50%> Min Daily Rest Period (Hours)";
                        group3Texts[1].text = "<size=50%> Max Daily Rest Period (Hours)";
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToStandard(); 
                        break;
                    case 17: //Initial Static Period
                        group3Texts[0].text = "<size=50%> Min Initial Static Period (Hours)";
                        group3Texts[1].text = "<size=50%> Max Initial Static Period (Hours)";
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToStandard(); 
                        break;
                    case 18: //Experiment Duration
                        group3Texts[0].text = "<size=50%> Min Duration (Days)";
                        group3Texts[1].text = "<size=50%> Max Duration (Days)";
                        group3Texts[2].text = "<size=50%> Default";
                        SetGroup3InputsOnAndToDefaultValues();
                        SetInputsToStandard(); 
                        break;
                }
                break;
        }
    }

    void SetGroup3InputsOnAndToDefaultValues()
    {
        group3Inputs[0].placeholder.GetComponent<TextMeshProUGUI>().text = currentSO.c.values.x.ToString();
        group3Inputs[0].gameObject.SetActive(true);
        group3Inputs[1].placeholder.GetComponent<TextMeshProUGUI>().text = currentSO.c.values.y.ToString();
        group3Inputs[1].gameObject.SetActive(true);
        group3Inputs[2].placeholder.GetComponent<TextMeshProUGUI>().text = currentSO.c.values.z.ToString();
        group3Inputs[2].gameObject.SetActive(true);
    }

    void SetSelectText(float typ)
    {
        selectText.text = typ switch
        {
            0 => "Sensor: ",
            1 => "Excreter: ",
            2 => "Eliminator: ",
            _ => "System Control: "
        };
    }

    void ValidateRelational(TMP_InputField changed)
    {
        // Identify the active fields 
        TMP_InputField minIF       = group3Inputs[0].gameObject.activeInHierarchy
                                     ? group3Inputs[0] : null;
        TMP_InputField maxIF       = null;
        TMP_InputField expectedIF  = null;

        for (int i = 1; i < group3Inputs.Length; i++)
        {
            if (!group3Inputs[i].gameObject.activeInHierarchy) continue;
            if (maxIF == null)          maxIF      = group3Inputs[i];
            else { expectedIF = group3Inputs[i];   break; }
        }

        // Nothing to validate yet
        if (minIF == null || maxIF == null) return;

        // Parse whatever numbers we already have
        bool hasMin = float.TryParse(minIF.text, out var minVal);
        bool hasMax = float.TryParse(maxIF.text, out var maxVal);
        float expVal = 0f;
        bool hasExp = expectedIF != null && float.TryParse(expectedIF.text, out expVal);

        // 1) Max must be greater than Min
        if (hasMin && hasMax && maxVal < minVal)
        {
            UIManager.Message("Max value must be greater than Min value.");
            if (changed == maxIF) maxIF.text = string.Empty;
            else                  minIF.text = string.Empty;
            return;
        }

        // 2) Expected must lie within [Min, Max]
        if (hasMin && hasMax && hasExp &&
            (expVal < minVal || expVal > maxVal))
        {
            UIManager.Message("Default value must lie between Min and Max.");
            if (changed == expectedIF) expectedIF.text = string.Empty;
            else                       changed.text    = string.Empty;
            return;
        }

        CheckInputs();
    }



    void SetInputsToStandard() //Any real number
    {
        foreach (var inp in group3Inputs)
        {
            if (!inp.gameObject.activeInHierarchy) continue;
            inp.contentType = TMP_InputField.ContentType.DecimalNumber;

            inp.onEndEdit.RemoveAllListeners();
            inp.onEndEdit.AddListener(text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return;

                if (!float.TryParse(text, out _))
                {
                    UIManager.Message("Please enter a valid number");
                    inp.text = string.Empty;
                    return;
                }

                ValidateRelational(inp);
            });
        }
    }

    void SetInputsToMediaExchange() //0-100%
    {
        foreach (var inp in group3Inputs)
        {
            if (!inp.gameObject.activeInHierarchy) continue;
            inp.contentType = TMP_InputField.ContentType.DecimalNumber;

            inp.onEndEdit.RemoveAllListeners();
            inp.onEndEdit.AddListener(text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return;

                if (!float.TryParse(text, out var val) || val < 0f || val > 100f)
                {
                    UIManager.Message("Enter a value between 0 and 100 (percentage)");
                    inp.text = string.Empty;
                    return;
                }

                ValidateRelational(inp);
            });
        }
    }
    
    void SetInputsToEccentricity() //0-1
    {
        foreach (var inp in group3Inputs)
        {
            if (!inp.gameObject.activeInHierarchy) continue;
            inp.contentType = TMP_InputField.ContentType.DecimalNumber;

            inp.onEndEdit.RemoveAllListeners();
            inp.onEndEdit.AddListener(text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return;

                if (!float.TryParse(text, out var val) || val < 0f || val > 1f)
                {
                    UIManager.Message("Eccentricity must be between 0 and 1");
                    inp.text = string.Empty;
                    return;
                }
                ValidateRelational(inp);
            });
        }
    }
    
    Color ColorFromTyp(int ind)
    {
        return ind switch
        {
            0 => new Color(101 / 255f, 139 / 255f, 166 / 255f),
            1 => new Color(95 / 255f, 159 / 255f, 107 / 255f),
            2 => new Color(166 / 255f, 89 / 255f, 79 / 255f),
            _ => new Color(159 / 255f, 157 / 255f, 95 / 255f)
        };
    }
    
    bool CheckInputs()
    {
        bool allFilled = true;
        foreach (TMP_InputField i in group3Inputs)
        {
            if(! i.gameObject.activeInHierarchy) continue;
            if (i.text == "")
            {
                allFilled = false;
                break;
            }
        }
        if (allFilled)
        {
            if (currentSO.c.type == 0)
            {
                float.TryParse(group3Texts[0].text, out var x);
                float.TryParse(group3Texts[1].text, out var y);
                float.TryParse(group3Texts[2].text, out var z);
                slid.SetRange(new Vector2(x,y),z);
                slid.SetNames("Range (mg/dL)");
                slid.gameObject.SetActive(true);
            }
        }
        return allFilled;
    }

    public void Confirm()
    {
        if (!CheckInputs())
        {
            UIManager.Message("Complete all global parameters first before confirming control");
            return;
        }
        foreach(TMP_InputField i in group3Inputs)
        {
            i.gameObject.SetActive(false);
        }
        for(int i = 0; i < 5; i++)
        {
            if(i==3)continue;
            group3Texts[i].gameObject.SetActive(false);
        }

        if (typInd > 3) typInd = 3;
        sos[typInd].Remove(currentSO);
        slid.gameObject.SetActive(false);
        SetupExperiment.i.AddControl(this);
        UIManager.Message("Added Control: " + currentSO.c.name,false);
        group3Buttons[0].gameObject.SetActive(false);
        plusMinus.onClick.AddListener(Deactivate);
        background.color = Color.Lerp(ColorFromTyp(currentSO.c.type > 3 ? 3 : currentSO.c.type), Color.black, 0.5f);
        //specifcy the type of control parameter that has been confirmed.
        SetSelectText(typInd);
        string s = selectText.text;
        selectText.text = currentSO.c.name + " " + s;
        group3Texts[3].text = selectText.text.Remove(selectText.text.Length - 2, 2);
    }
    

    private void ResetGroup3()
    {
        group3Buttons[0].GetComponentInChildren<TextMeshProUGUI>().text = "Confirm";
        group3Buttons[0].GetComponentInChildren<Image>().color = Color.white;
        group3Buttons[0].onClick.RemoveAllListeners();
        group3Buttons[0].onClick.AddListener(Confirm);
        group3Buttons[0].gameObject.SetActive(true);
        foreach(TMP_InputField i in group3Inputs)
        {
            i.gameObject.SetActive(true);
        }
        for(int i = 0; i < 5; i++)
        {
            group3Texts[i].gameObject.SetActive(true);
        }
    }

    public void Deactivate()
    {
        ResetGroup3();
        slid.gameObject.SetActive(true);
        background.color = new Color(31f/255f, 31f/255f, 31f/255f);
        SetupExperiment.i.RemoveControl(this);
        sos[typInd].Add(currentSO);
        
    }
    
    //Sensor: Name, Min (ml/L), Max (ml/L), Default (ml/L). | Startup Production Cost | Maintenance Cost. -> Triggers media exchange or excretion or elimination
    //Excreter: Name, Max ml per week (ml/day) | Production Cost, Price (£/L). Requires sensor.
    //Eliminator: Name, Max Cycle Per Day | Production Cost, Cost per cycle (£). 
    //Three float -> Int type, Vector3 & String
    
    [Serializable]
    public struct Control
    {
        public string name;
        public int type;
        public Vector3 values;
        public float startupCost;
        public float continuationCost;
        [FormerlySerializedAs("unitCost")] public float supplyCost;

        public enum Objective
        {
            maximise,
            minimise,
            range
        }

        public Objective objective;

        public enum measurement
        {
            mg_per_dL,
            mmol_per_L,
            ug_per_dL,
            mmHg,
            ml,
            mg,
            ug,
            g,
            unitless
        }
        
        //HIGHER ECCENTRICITY IS HIGHER PRESSURE

        public string Measure()
        {
            switch (unit)
            {
                case measurement.mg_per_dL:
                    return "(mg/dL)";
                case measurement.mmol_per_L:
                    return "(mmol/L)";
                case measurement.ug_per_dL:
                    return "(ug/dL)";
                case measurement.mmHg:
                    return "(mmHg)";
                case measurement.ml:
                    return "(ml)";
                case measurement.g:
                    return "(g)";
                case measurement.mg:
                    return "(mg)";
                case measurement.ug:
                    return "(ug)";
                default:
                    return "";
            }
        }
        public measurement unit;

        public Control(string nam, int typ, Vector3 val, float startupCos, float continuationCos, measurement m, float supplyCos, Objective obj)
        {
            name = nam;
            type = typ;
            values = val;
            startupCost = startupCos;
            continuationCost = continuationCos;
            unit = m;
            supplyCost = supplyCos;
            objective = obj;
        }
    }
    
    public Control GetControl()
    {
        float.TryParse(group3Inputs[0].text, out var x);
        float.TryParse(group3Inputs[1].text, out var y);
        float.TryParse(group3Inputs[2].text, out var z);
        return new Control(currentSO.c.name,
            currentSO.c.type,
            new Vector3(x,y,z),
            currentSO.c.startupCost,
            currentSO.c.continuationCost,
            currentSO.c.unit,
            currentSO.c.supplyCost,
            currentSO.c.objective);
    }
    
}
