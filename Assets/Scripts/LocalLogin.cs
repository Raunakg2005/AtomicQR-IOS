using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Security.Cryptography;
using System.Text;
using System.Collections;

public class LoginManager : MonoBehaviour
{
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;
    public Toggle rememberMeToggle;

    public RawImage successImage; // ✅ Login successful image
    public RawImage errorImage;   // ❌ Invalid credentials image

    private string correctUsername = "admin";
    private string correctPasswordHash = "5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8"; // hash of "password"

    void Start()
    {
        // Hide both images initially
        successImage.gameObject.SetActive(false);
        errorImage.gameObject.SetActive(false);

        // Optional: auto login
        if (PlayerPrefs.GetInt("rememberMe", 0) == 1)
        {
            string savedUsername = PlayerPrefs.GetString("username", "");
            string savedHash = PlayerPrefs.GetString("passwordHash", "");

            if (savedUsername == correctUsername && savedHash == correctPasswordHash)
            {
                Debug.Log("✅ Auto login successful!");
                UnityEngine.SceneManagement.SceneManager.LoadScene("ARScene");
            }
        }
    }

    public void OnLogin()
    {
        // Hide both images before starting
        successImage.gameObject.SetActive(false);
        errorImage.gameObject.SetActive(false);

        StartCoroutine(HandleLogin());
    }

    private IEnumerator HandleLogin()
    {
        yield return null;

        string enteredUsername = usernameInput.text.Trim();
        string enteredPassword = passwordInput.text;
        string enteredHash = ComputeSha256Hash(enteredPassword);

        if (enteredUsername == correctUsername && enteredHash == correctPasswordHash)
        {
            successImage.gameObject.SetActive(true);
            errorImage.gameObject.SetActive(false);

            if (rememberMeToggle != null && rememberMeToggle.isOn)
            {
                PlayerPrefs.SetInt("rememberMe", 1);
                PlayerPrefs.SetString("username", enteredUsername);
                PlayerPrefs.SetString("passwordHash", enteredHash);
            }
            else
            {
                PlayerPrefs.DeleteKey("rememberMe");
                PlayerPrefs.DeleteKey("username");
                PlayerPrefs.DeleteKey("passwordHash");
            }

            yield return new WaitForSeconds(1f);
            UnityEngine.SceneManagement.SceneManager.LoadScene("App");
        }
        else
        {
            errorImage.gameObject.SetActive(true);
            successImage.gameObject.SetActive(false);
        }
    }

    private string ComputeSha256Hash(string rawData)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            StringBuilder builder = new StringBuilder();
            foreach (byte b in bytes)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}