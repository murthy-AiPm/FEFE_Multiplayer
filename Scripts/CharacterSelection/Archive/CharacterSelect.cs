using UnityEngine;
public class CharacterSelect : MonoBehaviour
{
    public void SelectCharacter(int characterId)
    {
        PlayerPrefs.SetInt("SelectedCharacter", characterId);
        PlayerPrefs.Save();
        //UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterSelect"); // or Game
    }
}
