using System.Collections.Generic;
using UnityEngine;


public class HumanoidCombatController : CombatController
{
    [SerializeField] internal PlayerController controller;

    [Header("Tap Settings")]
    [SerializeField] private float multiTapWindow = 0.25f;   // Max time between taps
    [SerializeField] private float tapLatch = 0.25f;         // How long to keep result alive for animator

    private int _tapCount;
    private float _lastTapTime;
    private float _tapLatchUntil;

    public string clicks = "None";
    public GameObject singleHandSword;

    void Start()
    {
        weaponType = "Fists";
        singleHandSword.SetActive(false);
        clicks = "None";
    }

    protected override void Update()
    {
        base.Update();
        SelectWeapon();
        WeaponDraw();
        WeaponDamage();

        UpdatePrimaryTaps();   // ✅ replaced CheckForClicks

        attackLevel = 0;
    }

    private void UpdatePrimaryTaps()
    {
        // Expire latch
        if (Time.time > _tapLatchUntil)
            clicks = "None";

        if (!Input.GetButtonDown("PrimaryAttack"))
            return;

        // If too slow since last tap, start a new sequence
        if (Time.time - _lastTapTime > multiTapWindow)
            _tapCount = 0;

        _tapCount++;
        _lastTapTime = Time.time;

        // Decide immediately on tap (no waiting)
        if (weaponType == "SingleHandedSword")
        {
            if (_tapCount >= 3) clicks = "Triple";
            else if (_tapCount == 2) clicks = "Double";
            else clicks = "Single";
        }
        else // Fists
        {
            clicks = "Single";
        }

        // Latch so CombatAnimator can see it for multiple frames
        _tapLatchUntil = Time.time + tapLatch;
    }

    private void SelectWeapon()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            weaponType = "Fists";
            singleHandSword.SetActive(false);
            clicks = "None";
            _tapCount = 0;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            weaponType = "SingleHandedSword";
            clicks = "None";
            _tapCount = 0;
        }
    }

    private void WeaponDraw()
    {
        if (weaponType == "SingleHandedSword")
        {
            if (playerController.inputController.isCombatMode)
                singleHandSword.SetActive(true);
            else if (!playerController.inputController.isCombatMode && !playerController.inputController.isSheating)
                singleHandSword.SetActive(false);
        }
    }

    private int WeaponDamage()
    {
        attackPoints = (attackLevel > 1) ? 2 : 1;
        return attackPoints;
    }
}
