using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SubmitBatchResultsUI : MonoBehaviour
{
    [Header("UI refs")]
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private TMP_InputField valueInput;
    [SerializeField] private Button nextButton;

    private readonly List<float> entries = new (); // result0, cost0, result1, cost1..

    private int reactorIndex;   // which reactor we’re on
    private bool expectingCost; // false → expecting result ; true → cost

    void Awake()
    {
        nextButton.onClick.AddListener(HandleNext);
        valueInput.onSubmit.AddListener(_ => HandleNext());
    }
    
    private void HandleNext()
    {
        if (string.IsNullOrWhiteSpace(valueInput.text)) return;

        if (!TryParseAndStore()) return;          // invalid value early-out

        if (entries.Count == MasterAlgorithm.i.previousSets[^1].Count * 2)
        {
            MasterAlgorithm.i.SubmitBatchResults(entries);     // the key call
            // lock UI or give feedback here
            promptText.text = "Batch submitted!";
            valueInput.interactable = nextButton.interactable = false;
            return;
        }

        expectingCost = !expectingCost;           // flip result / cost
        if (!expectingCost) reactorIndex++;       
        PrepareNextPrompt();
    }

    private bool TryParseAndStore()
    {
        if (!expectingCost)                   
        {
            if (int.TryParse(valueInput.text, out int r) && r is >= 1 and <= 6)
            {
                entries.Add(r);
                return ClearInput();
            }
            promptText.text = "Result must be an integer 1-6 — try again";
            return false;
        }
        else                                   
        {
            if (float.TryParse(valueInput.text, out float c) && c >= 0f)
            {
                entries.Add(c);
                return ClearInput();
            }
            promptText.text = "Cost must be a positive number — try again";
            return false;
        }
    }

    private bool ClearInput()
    {
        valueInput.text = "";
        valueInput.ActivateInputField();
        return true;
    }

    public void PrepareNextPrompt()
    {
        string label = expectingCost
            ? $"Enter COST for Reactor {reactorIndex + 1}"
            : $"Enter RESULT (1-6) for Reactor {reactorIndex + 1}";
        promptText.text = label;
        valueInput.placeholder.GetComponent<TMP_Text>().text =
            expectingCost ? "e.g. 123.45" : "1 … 6";
        valueInput.ActivateInputField();
        if (reactorIndex >= MasterAlgorithm.numReactors)
        {
            Submit();
        }
    }

    public void Disable()
    {
        
    }

    public void Submit()
    {
        MasterAlgorithm.i.SubmitBatchResults(entries);
        gameObject.SetActive(false);
    }
}