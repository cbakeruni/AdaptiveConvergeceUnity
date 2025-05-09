using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SliderGroup : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI[] texts;
    [SerializeField] public Slider slid;

    public void SetRange(Vector2 v, float val)
    {
        slid.maxValue = v.y;
        slid.minValue = v.x;
        slid.value = val;
        texts[0].text = v.x.ToString("F1");
        texts[1].text = v.y.ToString("F1");
        texts[2].text = slid.value.ToString("F1");
    }

    public void SetNames(string nam)
    {
        texts[3].text = nam;
    }
}
