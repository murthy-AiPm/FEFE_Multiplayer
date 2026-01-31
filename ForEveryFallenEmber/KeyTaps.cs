using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KeyTaps : MonoBehaviour
{
    float ClickDelay = 0.2f;
    int _currClicks;
    float _clickTime;
    string button;
    int k;
    public string clicks;


    void Update()
    {

        CheckForClicks("PrimaryAttack",0.5f);
    }

    private void CheckForClicks( string buttonName, float clickDelay)
    {

        if (Input.GetButtonDown(buttonName))
        {
            _currClicks++;


            _clickTime = 0;
        }

        if (_currClicks == 0) return;


        if (_clickTime < ClickDelay)
        {
            _clickTime += Time.deltaTime;
            return;
        }


        HandleClicks(_currClicks);
        _currClicks = 0;
        _clickTime = 0;

    }

    private void HandleClicks(int amountOfClicks)
    {

        switch (amountOfClicks)
        {
            case 1:
                k = 1;
                clicks = "Single";

                break;
            case 2:
                k = 2;
                clicks = "Double";
                break;
            case 3:
                k = 3;
                clicks = "Triple";
                break;
            //case 4:
            //    clicks = "single";
            //    break;
            default:
                clicks = "None";
                break;
        }
    }
}
