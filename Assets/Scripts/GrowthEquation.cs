using UnityEngine;

/// A *very* simple, example “grading rubric”
public class GrowthEquation : MonoBehaviour
{
    // Map a [0‥1] quality score onto an integer grade 1‥6. the GA calls this once per reactor at the end)

    public float[] serumOptimals = new[] { 40f, 350, 15f, 900, 350, 100, 80f, 0.8f, 40f, 4f, 136f, 3000f, 1f, 1f, 1f };

    public float[] mechOptimals = new[]
        { 0.0003f, 35f, 0.6f, 0.95f, 80f, 210f, 1000f, 2700f, 2700f, 1000f, 20000f, 6000f, 2_000_000f };
    
    
    public int Assess(float[] averageVariables, float[] controls)
    {
        float qualityScore = 0f;
        float mechScore = 0f;
        for (int i = 0; i < averageVariables.Length; i++)
        {
            qualityScore += Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(serumOptimals[i] - averageVariables[i]) / serumOptimals[i]),2); //makes small values larger
        }
        qualityScore /= averageVariables.Length;
        Debug.Log("Quality Score: " + qualityScore);
        for (int i = 0; i < controls.Length; i++)
        {
            mechScore += Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(mechOptimals[i] - controls[i]) / mechOptimals[i]),2); //makes small values larger
        }
        mechScore /= controls.Length;
        Debug.Log("Motor Score: " + mechScore);
        Debug.Log("Score: " + qualityScore * mechScore * 6f + Random.Range(0f,2f));
        return Mathf.Clamp(Mathf.RoundToInt(qualityScore * mechScore * 6f + Random.Range(0f,2f)), 1, 6);
    }
}