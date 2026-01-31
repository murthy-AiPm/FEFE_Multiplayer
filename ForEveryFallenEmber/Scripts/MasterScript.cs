using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

public class MasterScript : MonoBehaviour
{
    [SerializeField] public GameObject Humanoid;
    [SerializeField] public  GameObject Dragon;
    [SerializeField] public GameObject humanUI;
    [SerializeField] public GameObject dragonUI;
    [SerializeField] public CinemachineFreeLook vcam;
    [SerializeField] bool startDragon =false;

    public string charcterIndicator;
 void Start()
    {
        if (startDragon == true)
        {
            Dragon.SetActive(true);
            dragonUI.SetActive(true);
            Humanoid.SetActive(false);
            humanUI.SetActive(false);
        }
        else
        {
            Dragon.SetActive(false);
            dragonUI.SetActive(false);
            Humanoid.SetActive(true);
            humanUI.SetActive(true);
        }

       
    }


    void Update()
    {
        ChangeCharacter();

        if (Dragon.activeInHierarchy == false)
        {
            Dragon.transform.position = Humanoid.transform.position;
            Dragon.transform.rotation = Humanoid.transform.rotation;
            charcterIndicator = "human";
        }
        else if (Humanoid.activeInHierarchy == false)
        {
            Humanoid.transform.position = Dragon.transform.position;
            Humanoid.transform.rotation = Dragon.transform.rotation;
            charcterIndicator = "dragon";
        }
    }

    private void ChangeCharacter()
    {
        if (Input.GetKey(KeyCode.Alpha0))
        {
            Dragon.SetActive(false);
            dragonUI.SetActive(false);
            Humanoid.SetActive(true);
            humanUI.SetActive(true);

        }
        else if (Input.GetKey(KeyCode.Alpha9))
        {
            Dragon.SetActive(true);
            dragonUI.SetActive(true);
            Humanoid.SetActive(false);
            humanUI.SetActive(false);
        }
    }
}
