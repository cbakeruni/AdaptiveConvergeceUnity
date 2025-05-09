using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Motor : MonoBehaviour
{
    public float RPM;
    public float eccentricity;

    public const float slowDownFactor = 0.0001f;
    public List<Log> logs  = new List<Log>();
    public Bioreactor r;

    // Update is called once per frame
    void Update()
    {
        transform.Rotate(Vector3.right, RPM * 360f / 60f * Time.deltaTime * Simulation.timeScale * slowDownFactor);
    }

    public void SetOn(float rpm, float ec, float duration)
    {
        logs.Add(new Log(rpm,ec,duration));
        RPM = rpm; 
        eccentricity = ec;
    }
    
    public struct Log
    {
        public float RPM;
        public float eccentricity;
        public float duration;
        public Log(float RPM, float eccentricity, float duration)
        {
            this.RPM = RPM;
            this.eccentricity = eccentricity;
            this.duration = duration;
        }
    }

    public float DetermineActivity()
    {
        float onTime = logs.Sum(x => x.duration);
        float offTime = r.perExperimentDuration - onTime;
        float averageEccentricity = logs.Average(x => x.eccentricity);
        float averageRPM = logs.Average(x => x.RPM);
        return averageRPM * averageEccentricity * onTime / (onTime + offTime);
    }
    
}
