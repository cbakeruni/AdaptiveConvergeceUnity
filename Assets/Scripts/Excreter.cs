using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Excreter : MonoBehaviour
{
    [SerializeField] public Serum serum;
    [SerializeField] public string variable;

    [Header("Daily Excretion Limits (units per day)")]
    [SerializeField] public float minDailyRate = 0f;   
    [SerializeField] public float maxDailyRate = 0f;  

    [SerializeField] public float useCost;            
    [SerializeField] public Bioreactor reactor; 

    private float excretedToday = 0f;           
    private float nextDayReset  = 0f;            
    private bool  boosted       = false;        

    private const float secondsPerDay = 86400f;

    private void Awake()
    {
        nextDayReset = Simulation.timer + secondsPerDay;
    }

   
    public void RequestBoost() => boosted = true;

    private void Update()
    {
        // Reset the daily counter if we've entered a new simulated day
        if (Simulation.timer >= nextDayReset)
        {
            excretedToday = 0f;
            boosted       = false;
            nextDayReset += secondsPerDay;
        }

        // Determine how much we should excrete today
        float targetDailyRate = boosted ? maxDailyRate : minDailyRate;
        if (excretedToday >= targetDailyRate) return;    

        // Convert the daily target into a per‑frame step (scaled by deltaTime & timeScale)
        float step = targetDailyRate * Simulation.timeScale * Time.deltaTime / secondsPerDay;
        step = Mathf.Min(step, targetDailyRate - excretedToday); // clamp so we never overshoot

        // Apply excretion and accumulate cost
        serum.variables[variable] += step;
        reactor.extraCosts        += useCost * step;
        excretedToday             += step;

        // If just hit the boosted limit, turn the boost off for the remainder of the day
        if (boosted && excretedToday >= targetDailyRate)
            boosted = false;
    }
}