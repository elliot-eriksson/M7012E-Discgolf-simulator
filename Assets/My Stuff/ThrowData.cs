using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ThrowData : MonoBehaviour
{
    public float timeOfThrow;
    public float throwTimeDelta;
    public Dictionary<string, double> throwDictionary = null;


    public ThrowData(float timeOfThrow, float throwTimeDelta, Dictionary<string, double> throwDictionary)
    {
        this.timeOfThrow = timeOfThrow;
        this.throwTimeDelta = throwTimeDelta;
        this.throwDictionary = throwDictionary;
     
    }
}
