using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StartMenu : MonoBehaviour
{
    public static string password;
    public static string username;
    public static bool isNewConfiguration = false;

    [SerializeField] private TMP_InputField[] ifs;
    [SerializeField] private Button[] buttons;
    [SerializeField] private TextMeshProUGUI[] txts;
    
    public static bool newExperiment = false;
    public static bool simulation = false;
    
    public void LoadConfiguration()
    {
        ifs[1].gameObject.SetActive(true);
        LeanTween.scale(ifs[1].gameObject, Vector3.one * 2f, 1f).setEaseOutBack();
        ifs[3].gameObject.SetActive(true);
        LeanTween.scale(ifs[3].gameObject, Vector3.one * 2f, 1f).setEaseOutBack();
        txts[1].text = "Confirm";
        buttons[1].onClick.RemoveAllListeners();
        buttons[1].onClick.AddListener(() => CheckPasswordAndLoad(1));
        RefreshOtherButton(0);
    }

    public void Quit()
    {
        Application.Quit();
    }

    public void NewConfiguration()
    {
        ifs[0].gameObject.SetActive(true);
        LeanTween.scale(ifs[0].gameObject, Vector3.one * 2f, 1f).setEaseOutBack();
        ifs[2].gameObject.SetActive(true);
        buttons[2].gameObject.SetActive(true);
        LeanTween.scale(buttons[2].gameObject, Vector3.one * 2f, 1f).setEaseOutBack();
        LeanTween.scale(ifs[2].gameObject, Vector3.one * 2f, 1f).setEaseOutBack();
        txts[0].text = "Confirm";
        buttons[0].onClick.RemoveAllListeners();
        buttons[0].onClick.AddListener(() => CheckPasswordAndLoad(0));
        RefreshOtherButton(1);
        buttons[2].onClick.RemoveAllListeners();
        buttons[2].onClick.AddListener(() =>
        {
            simulation = true;
            CheckPasswordAndLoad(0);
        });
    }
    

    void RefreshOtherButton(int ind)
    {
        ifs[ind].gameObject.SetActive(false);
        ifs[ind+2].gameObject.SetActive(false);
        ifs[ind].transform.localScale = Vector3.zero;
        ifs[ind+2].transform.localScale = Vector3.zero;
        txts[ind].text = ind == 0 ? "New Configuration" : "Load Configuration";
        buttons[ind].onClick.RemoveAllListeners();
        buttons[ind].onClick.AddListener(() =>
        {
            if (ind == 0)
                NewConfiguration();
            else
                LoadConfiguration();
        });
        if (ind == 0)
        {
            buttons[2].gameObject.SetActive(false);
            buttons[2].transform.localScale = Vector3.zero;
        }
    }
    
    void CheckPasswordAndLoad(int ind)
    {
        if (ifs[ind].text != "" && ifs[ind + 2].text != "")
        {
            password = ifs[ind].text;
            username = ifs[ind+2].text;
            if (ind == 1)
            {
                // Attempt to decrypt an existing configuration using the entered password
                List<ControlParameter.Control> dummyControls;
                float[] dummyGlobals;
                bool passwordExists = SaveManager.Load(password, out dummyControls, out dummyGlobals);
                if (passwordExists)
                {
                    isNewConfiguration = false;
                    SceneManager.LoadScene(2);
                }
                else
                {
                    UIManager.Message("No configuration found with this password");
                }
            }
            else
            {
                isNewConfiguration = true;
                SceneManager.LoadScene(1);
            }
        }
        else
        {
            UIManager.Message("Please enter a name and password");
        }
    }
    

    private void Start()
    {
        newExperiment = false;
        password = "";
        username = "";
        for (int i = 0; i < 4; i++)
        {
            ifs[i].text = "";
        }
    }
}
