using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Serum : MonoBehaviour
{
    public float temperature;
    
    public Dictionary<string,float> variables = new ();
    public float proteoglycan;
    public float hyaluronan;
    public float igf1;
    
    public float albumin;
    public float calcium;
    public float chloride;
    public float glucose;
    public float igg;
    public float oxygen;
    public float potassium;
    public float sodium;
    public float protein;

    public float urea;
    public float lactate;
    public float ammonia;

    public float ml;


    public void Reset()
    {
        variables = new();
        variables.Add("Proteoglycan", proteoglycan);
        variables.Add("Hyaluronan", hyaluronan);
        variables.Add("IGF1", igf1);
        variables.Add("Albumin", albumin);
        variables.Add("Calcium", calcium);
        variables.Add("Chloride", chloride);
        variables.Add("Glucose", glucose);
        variables.Add("IGG", igg);
        variables.Add("Oxygen", oxygen);
        variables.Add("Potassium", potassium);
        variables.Add("Sodium", sodium);
        variables.Add("Protein", protein);
        variables.Add("Urea", urea);
        variables.Add("Lactate", lactate);
        variables.Add("Ammonia", ammonia);
    }

    public void MixInto(Serum s, float amount)
    {
        float newAmount = ml + s.ml;
        float ratio = amount / newAmount;
    
        // Create a temporary list of keys to avoid modifying the dictionary during enumeration
        List<string> keys = new List<string>(variables.Keys);
    
        // Update each value using the keys list for iteration
        foreach(var key in keys)
        {
            variables[key] = Mathf.Lerp(s.variables[key], variables[key], ratio);
        }
    
        ml -= amount;
    }

}
