using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sensor : MonoBehaviour
{
    [SerializeField] public Serum serum;
    [SerializeField] public string variable;
    public Vector2 range;

    public bool Trigger()
    {
        return serum.variables[variable] < range.x || serum.variables[variable] > range.y;
    }
}
