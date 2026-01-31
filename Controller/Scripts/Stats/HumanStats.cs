using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
//using MedievalKingdomUI;

public class HumanStats : MonoBehaviour
{
    public Slider staminaSlider;
    void Start()
    {
   
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void CurrentStamina(float stamina)
    {
        staminaSlider.value = stamina;
        // fill.color = gradient.Evaluate(speedSlider.normalizedValue);
    }
    public void SetMinStamina(float minStamina)
    {
        staminaSlider.minValue = minStamina;

    }

    public void SetMaxStamina(float maxStamina)
    {
        staminaSlider.maxValue = maxStamina;

    }
}
