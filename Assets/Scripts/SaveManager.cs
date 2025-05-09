using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEngine;
using System.Text;
using System.Linq;

public static class SaveManager
{
    // Register JSON converter for  whole application
    static SaveManager()
    {
        JsonConvert.DefaultSettings = () =>
        {
            var settings = new JsonSerializerSettings();
            settings.Converters.Add(new Vector3Converter());
            return settings;
        };
    }
    // Build a unique save‑file path for every experiment by embedding the username in the file name.
    private static string GetControlsPath()
    {
        string user = StartMenu.username;
        if (string.IsNullOrEmpty(user)) user = "default";
        var invalid = Path.GetInvalidFileNameChars();
        var safeUser = new string(user.Where(c => !invalid.Contains(c)).ToArray());
        return Path.Combine(Application.persistentDataPath, $"{safeUser}_controls.dat");
    }
    
    public static void Save(List<ControlParameter.Control> controls,
        float[] globals,
        string password)
    {
        var payload = new SaveData { Controls = controls, Globals = globals };
        string json = JsonConvert.SerializeObject(payload);
        byte[] encrypted = Encrypt(json, password);
        string path = GetControlsPath();
        File.WriteAllBytes(path, encrypted);
    }
    

    public static bool Load(string       password,
        out List<ControlParameter.Control> controls,
        out float[]  globals)
    {
        controls = new List<ControlParameter.Control>();
        globals  = Array.Empty<float>();

        string path = GetControlsPath();
        if (!File.Exists(path)) return false;

        try
        {
            byte[] encrypted = File.ReadAllBytes(path);
            string json = Decrypt(encrypted, password);

            var payload = JsonConvert.DeserializeObject<SaveData>(json);
            if (payload == null) return false;               // corrupt file?

            controls = payload.Controls ?? controls;
            globals  = payload.Globals  ?? globals;
            return true;
        }
        catch (CryptographicException e)
        {
            Debug.LogWarning($"SaveManager.Load – decryption failed: {e.Message}");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveManager.Load – unexpected error: {e.Message}");
            return false;
        }
    }
    
    private static byte[] Encrypt(string data, string password)
    {
        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 256;
            byte[] salt = GenerateSalt();
            byte[] key = DeriveKey(password, salt);
            aes.Key = key;
            aes.GenerateIV();

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(salt, 0, salt.Length);
                ms.Write(aes.IV, 0, aes.IV.Length);

                using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                using (StreamWriter sw = new StreamWriter(cs))
                {
                    sw.Write(data);
                }

                return ms.ToArray();
            }
        }
    }

    private static string Decrypt(byte[] encryptedData, string password)
    {
        if (encryptedData == null || encryptedData.Length < 32)
            throw new CryptographicException("Encrypted data is too short—save file is likely corrupt or truncated.");
        using (MemoryStream ms = new MemoryStream(encryptedData))
        {
            byte[] salt = new byte[16];
            ms.Read(salt, 0, salt.Length);

            byte[] iv = new byte[16];
            ms.Read(iv, 0, iv.Length);

            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Key = DeriveKey(password, salt);
                aes.IV = iv;

                using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                using (StreamReader sr = new StreamReader(cs))
                {
                    return sr.ReadToEnd();
                }
            }
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256))
        {
            return pbkdf2.GetBytes(32);
        }
    }

    private static byte[] GenerateSalt()
    {
        byte[] salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        return salt;
    }
    
    // Generic object‑persistence helpers
    public static void SaveObject<T>(string fileName, T data, string password)
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        string json = JsonConvert.SerializeObject(data);
        byte[] encrypted = Encrypt(json, password);
        File.WriteAllBytes(path, encrypted);
    }
    
    public static void SaveInstruction(string fileBaseName, int generation, List<MasterAlgorithm.ReactorInput> reactors, string algorithmType)
    {
        // Build dated filename in the project data folder (non‑persistent)
        string dateStamp = DateTime.Now.ToString("yyyy-MM-dd");
        string safeBase  = Path.GetFileNameWithoutExtension(fileBaseName);
        if (string.IsNullOrEmpty(safeBase)) safeBase = "Instructions";
        string fileName  = $"{safeBase}_{dateStamp}.txt";
        string path      = Path.Combine(Application.dataPath, fileName);

        var sb = new StringBuilder();
        sb.AppendLine($"Generation: {generation}");
        sb.AppendLine($"Algorithm: {algorithmType}");
        sb.AppendLine($"Date: {dateStamp}");
        sb.AppendLine();

        for (int i = 0; i < reactors.Count; i++)
        {
            var r = reactors[i];
            sb.AppendLine($"# {i}");

            // System Controls
            if (r.systemControls != null)
            {
                foreach (var sc in r.systemControls.OrderBy(c => c.name))
                    sb.AppendLine($"System Control:  {sc.name}: {sc.value:F3}");
            }

            // Sensors (min,nax)
            if (r.sensors != null)
            {
                foreach (var s in r.sensors.OrderBy(s => s.name))
                    sb.AppendLine($"Sensor:  {s.name}: [{s.minValue:F3}, {s.maxValue:F3}]");
            }

            // Excretors (dailyRate)
            if (r.excreters != null)
            {
                foreach (var e in r.excreters.OrderBy(e => e.name))
                    sb.AppendLine($"Excreter:  {e.name}: {e.dailyRate:F3}");
            }

            // Elimators (on/off)
            if (r.eliminators != null)
            {
                foreach (var el in r.eliminators.OrderBy(el => el.name))
                    sb.AppendLine($"Eliminator:  {el.name}: ON");
            }

            sb.AppendLine();
        }
        UIManager.Message("Instructions saved to " + path, false);
        // Append if today’s file already exists
        if (File.Exists(path))
        {
            sb.Insert(0, Environment.NewLine);          // tidy separator
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
        }
        else
        {
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        Debug.Log("Written");
    }


    public static bool LoadObject<T>(string fileName, string password, out T data)
    {
        data = default;
        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (!File.Exists(path)) return false;

        try
        {
            byte[] encrypted = File.ReadAllBytes(path);
            string json = Decrypt(encrypted, password);
            data = JsonConvert.DeserializeObject<T>(json);
            return data != null;
        }
        catch (CryptographicException e)
        {
            Debug.LogWarning($"SaveManager.LoadObject – decryption failed for {fileName}: {e.Message}");
            return false;
        }
        catch (JsonException je)
        {
            Debug.LogWarning($"SaveManager.LoadObject – JSON parse error for {fileName}: {je.Message}");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveManager.LoadObject – unexpected error for {fileName}: {e.Message}");
            return false;
        }
    }

    [Serializable]
    private sealed class SaveData
    {
        public List<ControlParameter.Control> Controls;
        public float[] Globals;
    }
}
