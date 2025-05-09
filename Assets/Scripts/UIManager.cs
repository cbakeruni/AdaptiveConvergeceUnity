using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI txtPrefab;
    [SerializeField] private CanvasGroup[] cs; //Main Menu, Setup, Visualise, Record, Overlay
    public static UIManager i;
    private List<RectTransform> ts = new List<RectTransform>();
    private CanvasGroup current;
    public UnityEvent OnExit;
    private void Awake()
    {
        i = this;
        current = cs[0];
    }

    public static void Message(string s, bool? negative = true)
    {
        if(s.Length > 14)
        {
            if (s[0] == 'M')
            {
                if (s[1..4] == "ain")
                {
                    return;
                }
            }
            else if (s[1] == 'M')
            {
                if (s[2..5] == "ain")
                {
                    return;
                }
            }
        }

        Color c;
        if (negative.HasValue)
        {
            c = negative.Value ? Color.red : Color.green;
        }
        else
        {
            c = Color.yellow;
        }
        i.StartCoroutine(i.Msg(s,c));
    }

    private IEnumerator Msg(string s, Color c)
    {
        foreach (RectTransform t in ts)
        {
            t.anchoredPosition -= new Vector2(0f, 100f);
        }
        var txt = Instantiate(txtPrefab, Vector3.zero,Quaternion.identity, cs[4].transform);
        txt.rectTransform.anchoredPosition = new Vector2(0f, -100f);
        ts.Add(txt.rectTransform);
        txt.text = s;
        txt.color = c;
        txt.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(0.25f);
        for (float t = 0f; t < 4f; t += Time.unscaledDeltaTime)
        {
            yield return new WaitForSecondsRealtime(0f);
            if (t > 2f)
            {
                txt.color = Color.Lerp(txt.color, c, Time.unscaledDeltaTime * 2.5f * (t-2f));
            }
            txt.rectTransform.anchoredPosition -= new Vector2(0f, 10f * Time.unscaledDeltaTime);
        }
        ts.Remove(txt.rectTransform);
        Destroy(txt);
    }

    public void OnCanvas(CanvasGroup c)
    {
        if (c == current)
        {
            Debug.LogWarning("Already on this canvas");
            return;
        }
        OnExit.Invoke();
        OnExit.RemoveAllListeners();
        current.gameObject.SetActive(false);
        current.alpha = 0f;
        c.alpha = 0f;
        c.gameObject.SetActive(true);
        current = c;
        LeanTween.alpha(c.gameObject, 1f, 0f).setEaseOutBack();
    }

    public void LoadMainMenu() //called in buttons
    {
        SceneManager.LoadScene(0);
    }
}