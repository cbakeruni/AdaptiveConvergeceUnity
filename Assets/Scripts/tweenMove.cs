using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class tweenMove : MonoBehaviour
{

    [SerializeField] private Transform to;
    void Start()
    {
        LeanTween.move(gameObject,to,5f).setEaseOutCubic().setDelay(0.2f);
        LeanTween.rotate(gameObject, to.rotation.eulerAngles, 4f).setEaseOutCubic().setDelay(0.2f);
    }

    
}
