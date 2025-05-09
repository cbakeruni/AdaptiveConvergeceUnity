using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotator : MonoBehaviour
{

    private float speed = 0f;

    private void Start()
    {
        LeanTween.value(gameObject, 0f, 20f, 5f).setOnUpdate(x => speed = x).setEaseInSine().setDelay(3f);
    }

    void Update()
    {
        transform.Rotate(Vector3.up * (Time.deltaTime * speed));
    }
}
