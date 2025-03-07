using UnityEngine;

public class ButtonToggle : MonoBehaviour
{
    public GameObject objectToToggle; // Assign the GameObject in the Inspector

    // This function will show the object
    public void ShowObject()
    {
        if (objectToToggle != null)
        {
            objectToToggle.SetActive(true);
        }
    }

    // This function will hide the object
    public void HideObject()
    {
        if (objectToToggle != null)
        {
            objectToToggle.SetActive(false);
        }
    }

    // Optional: This function will toggle visibility
    public void ToggleObject()
    {
        if (objectToToggle != null)
        {
            objectToToggle.SetActive(!objectToToggle.activeSelf);
        }
    }
}
