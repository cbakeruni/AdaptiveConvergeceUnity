using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Eliminator : MonoBehaviour
{
    public Serum serum;
    [SerializeField] private string variable;
    [SerializeField] private float hourlyReduce;

    private void EliminatePerHour()
    {
        serum.variables[variable] *= hourlyReduce;
    }
}
