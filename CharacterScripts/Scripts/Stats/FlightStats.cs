using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//using MedievalKingdomUI;
using UnityEngine.UI;


public class FlightStats : MonoBehaviour
{
    public Slider speedSlider, staminaSlider;
    public Gradient gradient;
   // public Image fill;
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void CurrentSpeed(float airSpeed)
    {
        speedSlider.value = airSpeed;
       // fill.color = gradient.Evaluate(speedSlider.normalizedValue);
    }

    public void CurrentStamina(float stamina)
    {
        staminaSlider.value = stamina;
        // fill.color = gradient.Evaluate(speedSlider.normalizedValue);
    }

    //public void CurrentMomentum(float momentum)
    //{
    //    momentumSlider.value = momentum;
    //    // fill.color = gradient.Evaluate(speedSlider.normalizedValue);
    //}

    public void SetMaxAirSpeed(float maxairSpeed)
    {
        speedSlider.maxValue = maxairSpeed;

    }
    public void SetMinAirSpeed(float minairSpeed)
    {
        speedSlider.minValue = minairSpeed;

    }

    public void SetMinStamina(int minStamina)
    {
        staminaSlider.minValue = minStamina;

    }

    public void SetMaxStamina(float maxStamina)
    {
        staminaSlider.maxValue = maxStamina;

    }
}
