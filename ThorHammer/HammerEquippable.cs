#if IL2CPP
using Il2CppScheduleOne;
using Il2CppScheduleOne.Audio;
using Il2CppScheduleOne.Combat;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.FX;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Noise;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Vision;
using Il2CppScheduleOne.Weather;
#else
using ScheduleOne;
using ScheduleOne.Audio;
using ScheduleOne.Combat;
using ScheduleOne.DevUtilities;
using ScheduleOne.Effects;
using ScheduleOne.Equipping;
using ScheduleOne.FX;
using ScheduleOne.ItemFramework;
using ScheduleOne.Noise;
using ScheduleOne.NPCs;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Vision;
using ScheduleOne.Weather;
#endif

using System;
using System.Collections;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using S1MAPI.Gltf;
using S1MAPI.Utils;
using System.Reflection;

namespace ThorHammer;

public class HammerEquippable : Equippable_Viewmodel
{
#if IL2CPP
    public HammerEquippable(IntPtr ptr) : base(ptr) { }
#endif

    // ── Melee stats ──
    private const float Range = 1.5f;
    private const float HitRadius = 0.3f;
    private const float SwingCooldown = 0.25f;
    private const float SwingDuration = 0.15f;
    private const float SwingAngle = 70f;
    private const float HitTime = 0.06f;
    // Punch: viewmodel wind-up; camera tilt on PlayerCamera.Camera (after game LateUpdate, see Harmony patch)
    private const float PunchChargeDuration = 0.18f;
    private const float PunchChargeHammerAngle = -36f;
    private const float PunchReleaseDuration = 0.22f;
    private const float PunchCamYawRight = 7.5f;      // stronger top-right feeling
    private const float PunchCamPitchUp = -6.1f;      // look up is negative pitch in Unity
    private const float PunchCamPitchDown = 6.8f;     // release dip
    private const float PunchCamSmoothTime = 0.045f;

    // ── Wind-up (right-click hold) ──
    private const float WindUpMaxSpinSpeed = 2000f;
    private const float WindUpSpinRampSeconds = 5f;
    private const float AimFOVReduction = 9f;
    private const float AimZoomDuration = 0.2f;

    // ── Flight ──
    private const float FlightGracePeriod = 0.5f;
    private const float FlightShakeAmount = 0.8f;
    private const float FlightFOVBoost = 12f;
    private const float FlightFOVDuration = 0.5f;
    private const float FlightMaxRoll = 25f;      // Max camera roll when turning (degrees)
    private const float FlightRollSpeed = 3f;     // How fast roll catches up to lateral movement
    private const float FlightImpactShakeMinSpeed = 12f;  // Below this speed, no shake
    private const float FlightImpactShakeMaxAmount = 1.35f; // 1.5x old max (0.9)
    private const float FlightImpactShakeCap = 1.35f;
    private const float FlightImpactBaseRadius = 4f;      // Radius at min speed (scales with landing speed)
    private const float FlightRollRecoveryDuration = 0.45f;
    private const float FlightShakeIntensity = 0.05f;     // StartCameraShake call
    private const float FlightCamShakePos = 0.001f;       // Direct camera position shake
    private const float FlightCamShakeRot = 0.15f;        // Direct camera rotation shake (degrees)

    // ── Throw ──
    private const float ThrowHitRadius = 0.3f;
    private const float ReturnCatchDistance = 0.5f;
    private const float SpinSpeed = 1440f;

    // ── State ──
    private enum HammerState { Idle, Charging, WindingUp, FlyingOut, FlyingBack, Flying }
    private HammerState _state = HammerState.Idle;

    // Charge hold (3s: rise + shake, lightning at 3s — no heavy rain required)
    private float _chargeHoldElapsed;
    private Vector3 _hammerModelBasePos;
    private const float ChargeSettleDelay = 0.9f; // wait before drop so shake is visible
    private const float ChargeSettleDuration = 0.6f; // 2x faster drop
    private float _chargeSettleElapsed;

    // Melee swing
    private float _cooldownRemaining;
    private bool _isSwinging;
    private float _swingElapsed;
    private bool _hitChecked;
    private bool _punchCharging;
    private float _punchChargeElapsed;
    private bool _punchReleasing;
    private float _punchReleaseElapsed;
    private Vector3 _punchCamRotSmoothed;
    private Vector3 _punchCamRotVel;
    private Vector3 _punchCamRotApplied;

    /// <summary>Equipped hammer instance — punch camera runs after PlayerCamera.LateUpdate via Harmony.</summary>
    internal static HammerEquippable ActiveInstance { get; private set; }

    // Model
    private GameObject _hammerModel;
    private GameObject _chargedModel;
    private Quaternion _modelBaseRotation;

    // Charge state (30s duration)
    private float _chargeTimeRemaining;

    // Hammer shake
    private float _hammerShakeElapsed;

    // Wind-up / aim
    private float _windUpElapsed;
    private bool _fovOverridden;

    // Lightning (hammer-to-target zap)
    private const float LightningAimRadius = 0.5f;
    private AudioClip _thunderClip;
    private bool _thunderClipSearched;
    private AudioClip _punchClip;
    private bool _punchClipSearched;
    private AudioClip _explosionClip;
    private bool _explosionClipSearched;

    // Throw projectile
    private GameObject _projectile;
    private Vector3 _throwDirection;
    private float _throwDistance;
    private float _throwSpeed; // outbound: constant until turn; turn: decel then accel back
    private float _returnSpeed; // return leg: ramps up from 0 when turning

    // Flight (momentum from charge duration, no stamina; speed scales with remaining time)
    private float _savedGravityMultiplier = 1f;
    private float _flightMomentumRemaining;
    private float _flightMomentumTotal;
    private float _flightStartTime;
    private Vector3 _flightVelocity;
    private float _flightShakePhase;
    private float _flightRoll;                    // Current camera roll for banking
    private float _flightRollRecoveryRemaining;    // Smooth roll return to 0 after landing

    /// <inheritdoc />
    public override void Equip(ItemInstance item)
    {
        gameObject.SetActive(true);

        localPosition = new Vector3(0.35f, -0.3f, 0.5f);
        localEulerAngles = new Vector3(0f, 0f, 0f);
        localScale = Vector3.one;

        LoadHammerModel();

#if IL2CPP
        // Inline base.Equip() — IL2CPP wrappers use il2cpp_object_get_virtual_method
        // which dispatches back to this override, causing infinite recursion.

        // Equippable.Equip:
        itemInstance = item;
        PlayerSingleton<PlayerInventory>.Instance.SetEquippable(this);
        PlayerSingleton<PlayerInventory>.Instance.EquippedSlotChanged();

        // Equippable_Viewmodel.Equip (continued):
        transform.localPosition = localPosition;
        transform.localEulerAngles = localEulerAngles;
        transform.localScale = localScale;
        LayerUtility.SetLayerRecursively(gameObject, LayerMask.NameToLayer("Viewmodel"));
        var renderers = gameObject.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var mr in renderers)
        {
            if (mr.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                mr.enabled = false;
            else
                mr.shadowCastingMode = ShadowCastingMode.Off;
        }
#else
        base.Equip(item);
#endif
        ActiveInstance = this;
    }

    /// <inheritdoc />
    public override void Unequip()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
        ClearPunchCameraOffset();
        if (_state == HammerState.Flying)
            StopFlight(false);
        if (_state == HammerState.WindingUp)
            CancelWindUp();
        CleanupThrow();
        _chargeTimeRemaining = 0f;
        SetChargedModel(false);

#if IL2CPP
        // Inline base.Unequip() — same virtual dispatch recursion issue.
        // Equippable.Unequip:
        PlayerSingleton<PlayerInventory>.Instance.SetEquippable(null);
        PlayerSingleton<PlayerInventory>.Instance.EquippedSlotChanged();
        UnityEngine.Object.Destroy(gameObject);
#else
        base.Unequip();
#endif
    }

    private void LoadHammerModel()
    {
        var glbData = EmbeddedResourceLoader.LoadBytes(
            "ThorHammer.Resources.ThorHammer.glb",
            Assembly.GetExecutingAssembly());

        if (glbData == null)
        {
            Melon<Core>.Logger.Error("Failed to load ThorHammer.glb from embedded resources");
            return;
        }

        _hammerModel = GltfLoader.LoadGlb(glbData);
        if (_hammerModel == null)
        {
            Melon<Core>.Logger.Error("GltfLoader.LoadGlb returned null");
            return;
        }

        _hammerModel.transform.SetParent(transform, false);
        _hammerModelBasePos = new Vector3(0.02f, -0.15f, 0.05f);
        _hammerModel.transform.localPosition = _hammerModelBasePos;
        _hammerModel.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        _hammerModel.transform.localScale = Vector3.one * 0.08f;
        _modelBaseRotation = _hammerModel.transform.localRotation;

        // Load charged model
        var chargedData = EmbeddedResourceLoader.LoadBytes(
            "ThorHammer.Resources.ThorHammerCharged.glb",
            Assembly.GetExecutingAssembly());
        if (chargedData != null)
        {
            _chargedModel = GltfLoader.LoadGlb(chargedData);
            if (_chargedModel != null)
            {
                _chargedModel.transform.SetParent(transform, false);
                _chargedModel.transform.localPosition = _hammerModelBasePos;
                _chargedModel.transform.localRotation = _hammerModel.transform.localRotation;
                _chargedModel.transform.localScale = _hammerModel.transform.localScale;
                _chargedModel.SetActive(false);
            }
        }
    }

    private GameObject ActiveHammerModel =>
        _chargeTimeRemaining > 0f && _chargedModel != null ? _chargedModel : _hammerModel;

    private void SetChargedModel(bool charged)
    {
        if (_hammerModel != null)
            _hammerModel.SetActive(!charged || _chargedModel == null);
        if (_chargedModel != null)
            _chargedModel.SetActive(charged);
    }

#if IL2CPP
    public
#else
    protected
#endif
    override void Update()
    {
#if !IL2CPP
        base.Update(); // IL2CPP: skipped — base is empty and virtual dispatch would recurse
#endif

        if (_cooldownRemaining > 0f)
            _cooldownRemaining -= Time.deltaTime;

        // Charge countdown
        if (_chargeTimeRemaining > 0f)
        {
            _chargeTimeRemaining -= Time.deltaTime;
            if (_chargeTimeRemaining <= 1f && _chargedModel != null && _chargedModel.activeSelf)
            {
                float shakeT = 1f - _chargeTimeRemaining;
                float a = Mathf.Lerp(0.5f, 1.8f, shakeT) * (1f - shakeT);
                float t = shakeT * 15f;
                _chargedModel.transform.localRotation = _modelBaseRotation * Quaternion.Euler(
                    Mathf.Sin(t) * a, Mathf.Sin(t * 1.1f + 1f) * a, Mathf.Sin(t * 0.9f) * a);
            }
            if (_chargeTimeRemaining <= 0f)
            {
                _chargeTimeRemaining = 0f;
                SetChargedModel(false);
            }
        }

        // Hammer settle: delay (shake only) then drop with ease-out (decelerate into place)
        if (_chargeSettleElapsed > 0f)
        {
            _chargeSettleElapsed -= Time.deltaTime;
            var settleModel = _chargedModel;
            if (settleModel != null && settleModel.activeSelf)
            {
                if (_chargeSettleElapsed <= ChargeSettleDuration)
                {
                    float rawT = 1f - Mathf.Clamp01(_chargeSettleElapsed / ChargeSettleDuration);
                    float easeOut = 1f - (1f - rawT) * (1f - rawT);
                    float y = Mathf.Lerp(0.1f, _hammerModelBasePos.y, easeOut);
                    settleModel.transform.localPosition = new Vector3(_hammerModelBasePos.x, y, _hammerModelBasePos.z);
                }
            }
        }

        if (_hammerShakeElapsed > 0f)
        {
            _hammerShakeElapsed -= Time.deltaTime;
            var shakeModel = ActiveHammerModel ?? _hammerModel;
            if (shakeModel != null)
            {
                float a;
                if (_chargeSettleElapsed > ChargeSettleDuration)
                    a = 3f;
                else if (_chargeSettleElapsed > 0f)
                {
                    float dropT = 1f - Mathf.Clamp01(_chargeSettleElapsed / ChargeSettleDuration);
                    a = Mathf.Lerp(2.5f, 0.3f, dropT);
                }
                else
                    a = 0.8f;
                float time = (1.5f - _hammerShakeElapsed) * 10f;
                shakeModel.transform.localRotation = _modelBaseRotation * Quaternion.Euler(
                    Mathf.Sin(time) * a, Mathf.Sin(time * 1.2f) * a, Mathf.Sin(time * 0.8f) * a);
            }
            if (_hammerShakeElapsed <= 0f && (ActiveHammerModel ?? _hammerModel) != null)
            {
                (ActiveHammerModel ?? _hammerModel).transform.localRotation = _modelBaseRotation;
                if (_chargedModel != null)
                    _chargedModel.transform.localPosition = _hammerModelBasePos;
            }
        }

        switch (_state)
        {
            case HammerState.Idle:
                UpdateSwingAnimation();
                UpdateIdle();
                break;
            case HammerState.Charging:
                UpdateCharging();
                break;
            case HammerState.WindingUp:
                UpdateWindUp();
                break;
            case HammerState.FlyingOut:
                UpdateFlyingOut();
                break;
            case HammerState.FlyingBack:
                UpdateFlyingBack();
                break;
            case HammerState.Flying:
                UpdateFlying();
                break;
        }
    }

    /// <summary>Called from Harmony postfix after PlayerCamera.LateUpdate (bob + reset already applied).</summary>
    internal static void ApplyPunchCameraAfterGameLateUpdate(PlayerCamera pc)
    {
        // Reverted per request: no extra punch camera motion.
    }

    private void ApplyPunchCameraToChild(PlayerCamera pc)
    {
        // Reverted per request: no extra punch camera motion.
    }

    private void ClearPunchCameraOffset()
    {
        _punchCamRotSmoothed = Vector3.zero;
        _punchCamRotVel = Vector3.zero;
        _punchCamRotApplied = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (_state == HammerState.Flying)
        {
            ApplyFlightRoll(_flightRoll);
            ApplyFlightCameraShake();
        }
        if (_flightRollRecoveryRemaining > 0f)
        {
            _flightRollRecoveryRemaining -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(_flightRollRecoveryRemaining / FlightRollRecoveryDuration);
            float tSmooth = 1f - (1f - t) * (1f - t) * (1f - t);
            float smoothed = Mathf.Lerp(_flightRoll, 0f, tSmooth);
            ApplyFlightRoll(smoothed);
            if (_flightRollRecoveryRemaining <= 0f)
            {
                _flightRollRecoveryRemaining = 0f;
                _flightRoll = 0f;
                ApplyFlightRoll(0f);
            }
        }
    }

    // ════════════════════════════════════════════
    //  IDLE (melee, start wind-up)
    // ════════════════════════════════════════════

    private void UpdateIdle()
    {
        bool canInteract = !GameInput.IsTyping &&
            PlayerSingleton<PlayerCamera>.Instance.activeUIElementCount == 0;

        // Right-click → start wind-up spin
        if (canInteract && GameInput.GetButtonDown(GameInput.ButtonCode.SecondaryClick))
        {
            StartWindUp();
            return;
        }

        // Lightning key (X): when charged → zap target; when not charged → hold to charge
        if (canInteract)
        {
            if (_chargeTimeRemaining > 0f && Input.GetKeyDown(Core.LightningKey))
            {
                if (Core.StaminaEnabled &&
                    PlayerSingleton<PlayerMovement>.Instance.CurrentStaminaReserve < Core.LightningStaminaCost)
                    return;
                if (Core.StaminaEnabled)
                    PlayerSingleton<PlayerMovement>.Instance.ChangeStamina(-Core.LightningStaminaCost);
                TryLightningZapToTarget();
                return;
            }
            if (Input.GetKey(Core.LightningKey))
            {
                StartChargeHold();
                return;
            }
        }

        // Left-click melee (hold to charge, release to hit)
        if (canInteract && !_isSwinging && _cooldownRemaining <= 0f && !_punchReleasing)
        {
            if (GameInput.GetButtonDown(GameInput.ButtonCode.PrimaryClick))
            {
                if (Core.StaminaEnabled &&
                    PlayerSingleton<PlayerMovement>.Instance.CurrentStaminaReserve < Core.SwingStaminaCost)
                    return;
                StartPunchCharge();
            }
            if (_punchCharging)
            {
                UpdatePunchCharge();
                return;
            }
        }
        if (_punchReleasing)
        {
            UpdatePunchRelease();
            return;
        }
    }

    // ════════════════════════════════════════════
    //  PUNCH CHARGING (hold: camera tilts top-right; release: dips down then resets)
    // ════════════════════════════════════════════

    private void StartPunchCharge()
    {
        _punchCharging = true;
        _punchChargeElapsed = 0f;
    }

    private void UpdatePunchCharge()
    {
        if (!GameInput.GetButton(GameInput.ButtonCode.PrimaryClick))
        {
            _punchCharging = false;
            _punchReleasing = true;
            _punchReleaseElapsed = 0f;
            _hitChecked = false;
            if (Core.StaminaEnabled)
                PlayerSingleton<PlayerMovement>.Instance.ChangeStamina(-Core.SwingStaminaCost);
            return;
        }
        _punchChargeElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_punchChargeElapsed / PunchChargeDuration);
        var chargeModel = ActiveHammerModel ?? _hammerModel;
        if (chargeModel != null)
        {
            // Pull back/right a bit to feel like shoulder+arm loading up, not only wrist.
            Vector3 chargePos = _hammerModelBasePos + new Vector3(0.04f * t, -0.02f * t, -0.07f * t);
            float yaw = Mathf.Lerp(0f, 16f, t);
            chargeModel.transform.localPosition = chargePos;
            chargeModel.transform.localRotation = Quaternion.Euler(t * PunchChargeHammerAngle, yaw, 0f) * _modelBaseRotation;
        }
    }

    private void UpdatePunchRelease()
    {
        _punchReleaseElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_punchReleaseElapsed / PunchReleaseDuration);
        float smoothT = 1f - (1f - t) * (1f - t);

        var swingModel = ActiveHammerModel ?? _hammerModel;
        if (swingModel != null)
        {
            float strike = Mathf.Sin(t * Mathf.PI);
            Vector3 chargePos = _hammerModelBasePos + new Vector3(0.04f, -0.02f, -0.07f);
            Vector3 impactPos = _hammerModelBasePos + new Vector3(-0.1f, -0.05f, 0.1f);
            if (t < 0.72f)
                swingModel.transform.localPosition = Vector3.Lerp(chargePos, impactPos, t / 0.72f);
            else
                swingModel.transform.localPosition = Vector3.Lerp(impactPos, _hammerModelBasePos, (t - 0.72f) / 0.28f);

            float angle = Mathf.Lerp(PunchChargeHammerAngle, 8f, smoothT) + strike * (SwingAngle + 12f);
            float yaw = Mathf.Lerp(16f, -34f, smoothT) + strike * 6f;
            swingModel.transform.localRotation = Quaternion.Euler(angle, yaw, 0f) * _modelBaseRotation;
        }

        if (!_hitChecked && t >= 0.6f)
        {
            _hitChecked = true;
            var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
            PlayPunchSound(cam.position + cam.forward * 2f);
            ExecuteMeleeHit();
        }
        if (t >= 1f)
        {
            _punchReleasing = false;
            _cooldownRemaining = SwingCooldown;
            var model = ActiveHammerModel ?? _hammerModel;
            if (model != null)
            {
                model.transform.localPosition = _hammerModelBasePos;
                model.transform.localRotation = _modelBaseRotation;
            }
        }
    }

    // ════════════════════════════════════════════
    //  MELEE SWING (legacy/fallback — now handled by punch charge)
    // ════════════════════════════════════════════

    private void StartSwing()
    {
        _isSwinging = true;
        _swingElapsed = 0f;
        _hitChecked = false;
        _cooldownRemaining = SwingCooldown;
    }

    private void UpdateSwingAnimation()
    {
        var swingModel = ActiveHammerModel ?? _hammerModel;
        if (!_isSwinging || swingModel == null)
            return;

        _swingElapsed += Time.deltaTime;
        float t = _swingElapsed / SwingDuration;

        if (t >= 1f)
        {
            _isSwinging = false;
            swingModel.transform.localRotation = _modelBaseRotation;
            return;
        }

        float angle = Mathf.Sin(t * Mathf.PI) * SwingAngle;
        swingModel.transform.localRotation = Quaternion.Euler(angle, 0f, 0f) * _modelBaseRotation;

        if (!_hitChecked && _swingElapsed >= HitTime)
        {
            _hitChecked = true;
            PlayPunchSound(PlayerSingleton<PlayerCamera>.Instance.transform.position + PlayerSingleton<PlayerCamera>.Instance.transform.forward * 2f);
            ExecuteMeleeHit();
        }
    }

    private void ExecuteMeleeHit()
    {
        if (!PlayerSingleton<PlayerCamera>.Instance.LookRaycast(
                Range, out var hit,
                NetworkSingleton<CombatManager>.Instance.MeleeLayerMask,
                includeTriggers: true, HitRadius))
        {
            return;
        }

        var damageable = hit.collider.GetComponentInParent<IDamageable>();
        if (damageable == null)
            return;

        bool charged = _chargeTimeRemaining > 0f;
        float force = charged ? Core.MeleeForce * Core.ChargedMeleeMultiplier : Core.MeleeForce;
        float damage = charged ? Core.MeleeDamage * Core.ChargedMeleeMultiplier : Core.MeleeDamage;

        var impact = new Impact(
            hit.point,
            PlayerSingleton<PlayerCamera>.Instance.transform.forward,
            force, damage,
            EImpactType.BluntMetal,
            Player.Local.NetworkObject,
            UnityEngine.Random.Range(int.MinValue, int.MaxValue));

        damageable.SendImpact(impact);
        Singleton<FXManager>.Instance.CreateImpactFX(impact, damageable);
        PlayerSingleton<PlayerCamera>.Instance.StartCameraShake(0.14f, 0.12f);

        if (charged)
        {
            var npc = hit.collider.GetComponentInParent<NPC>();
            if (npc != null)
            {
                Electrifying.ApplyToAvatar(npc.Avatar);
                MelonCoroutines.Start(LightningHelper.ClearElectrifyCoroutine(npc));
            }
        }
    }

    // ════════════════════════════════════════════
    //  WIND-UP (right-click hold → spin → throw or fly)
    // ════════════════════════════════════════════

    private void StartWindUp()
    {
        _state = HammerState.WindingUp;
        _windUpElapsed = 0f;
        _isSwinging = false;

        // Block jumping so space doesn't trigger a jump
        PlayerSingleton<PlayerMovement>.Instance.CanJump = false;

        // Zoom in
        float aimFov = Singleton<Settings>.Instance.CameraFOV - AimFOVReduction;
        PlayerSingleton<PlayerCamera>.Instance.OverrideFOV(aimFov, AimZoomDuration);
        _fovOverridden = true;
    }

    private void UpdateWindUp()
    {
        _windUpElapsed += Time.deltaTime;
        bool charged = _windUpElapsed >= Core.WindUpDuration;

        // Stamina drain during wind-up
        if (Core.StaminaEnabled)
        {
            PlayerSingleton<PlayerMovement>.Instance.ChangeStamina(-Core.WindUpStaminaRate * Time.deltaTime);
            if (PlayerSingleton<PlayerMovement>.Instance.CurrentStaminaReserve <= 0f)
            {
                CancelWindUp();
                return;
            }
        }

        // Accelerating spin: exponential ramp, max 2000 deg/s after 5 seconds
        float tau = WindUpSpinRampSeconds / 4.605f; // ~1.09: reaches 99% at 5s
        float spinSpeed = Mathf.Min(WindUpMaxSpinSpeed,
            WindUpMaxSpinSpeed * (1f - Mathf.Exp(-_windUpElapsed / tau)));
        var spinModel = ActiveHammerModel ?? _hammerModel;
        if (spinModel != null)
            spinModel.transform.Rotate(Vector3.forward, spinSpeed * Time.deltaTime, Space.Self);

        // Right-click released
        if (!GameInput.GetButton(GameInput.ButtonCode.SecondaryClick))
        {
            if (charged)
                StartThrow();
            else
                CancelWindUp();
            return;
        }

        // Space while charged → fly
        if (charged && GameInput.GetButton(GameInput.ButtonCode.Jump))
        {
            StartFlight();
        }
    }

    private void CancelWindUp()
    {
        _state = HammerState.Idle;
        _windUpElapsed = 0f;
        PlayerSingleton<PlayerMovement>.Instance.CanJump = true;

        if (_fovOverridden)
        {
            PlayerSingleton<PlayerCamera>.Instance.StopFOVOverride(AimZoomDuration);
            _fovOverridden = false;
        }

        if (_hammerModel != null)
            _hammerModel.transform.localRotation = _modelBaseRotation;
        if (_chargedModel != null)
            _chargedModel.transform.localRotation = _modelBaseRotation;
    }

    // ════════════════════════════════════════════
    //  THROW
    // ════════════════════════════════════════════

    private void StartThrow()
    {
        PlayerSingleton<PlayerMovement>.Instance.CanJump = true;

        if (_fovOverridden)
        {
            PlayerSingleton<PlayerCamera>.Instance.StopFOVOverride(AimZoomDuration);
            _fovOverridden = false;
        }

        var activeModel = ActiveHammerModel ?? _hammerModel;
        if (activeModel == null) return;

        activeModel.transform.localRotation = _modelBaseRotation;
        activeModel.SetActive(false);

        _projectile = UnityEngine.Object.Instantiate(activeModel);
        _projectile.SetActive(true);
        _projectile.transform.SetParent(null, false);
        SetLayerRecursive(_projectile, 0);

        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        _projectile.transform.position = cam.position + cam.forward * 1.0f;
        _projectile.transform.rotation = Quaternion.LookRotation(cam.forward) * Quaternion.Euler(0f, 90f, 0f);
        _projectile.transform.localScale = Vector3.one * 0.08f;

        _throwDirection = cam.forward;
        _throwDistance = 0f;
        _throwSpeed = Core.ThrowSpeed;

        _state = HammerState.FlyingOut;
    }

    private void UpdateFlyingOut()
    {
        if (_projectile == null)
        {
            CatchHammer();
            return;
        }

        float maxRange = Core.MaxThrowRange;
        float decelStart = 0.8f * maxRange;
        const float TurnThreshold = 0.5f;

        if (_throwDistance >= maxRange - TurnThreshold)
        {
            var target = PlayerSingleton<PlayerCamera>.Instance.transform.position;
            _throwDirection = (target - _projectile.transform.position).normalized;
            _returnSpeed = Core.ThrowSpeed;
            _state = HammerState.FlyingBack;
            return;
        }

        if (_throwDistance < decelStart)
        {
            _throwSpeed = Core.ThrowSpeed;
        }
        else
        {
            float remaining = maxRange - _throwDistance;
            float range = 0.2f * maxRange;
            float factor = (remaining / range) * (remaining / range);
            _throwSpeed = Core.ThrowSpeed * factor;
        }

        float step = Mathf.Min(_throwSpeed * Time.deltaTime, maxRange - _throwDistance - 0.01f);
        if (step < 0f) step = 0f;
        _throwDistance += step;
        Vector3 prevPos = _projectile.transform.position;

        _projectile.transform.position += _throwDirection * step;
        _projectile.transform.Rotate(Vector3.forward, SpinSpeed * Time.deltaTime, Space.Self);

        if (step > 0.001f && Physics.SphereCast(
                prevPos,
                ThrowHitRadius,
                _throwDirection,
                out var hit,
                step,
                NetworkSingleton<CombatManager>.Instance.MeleeLayerMask,
                QueryTriggerInteraction.Collide))
        {
            ExecuteThrowHit(hit);
            _returnSpeed = Core.ThrowSpeed;
            _state = HammerState.FlyingBack;
            return;
        }

    }

    private void UpdateFlyingBack()
    {
        if (_projectile == null)
        {
            CatchHammer();
            return;
        }

        var target = PlayerSingleton<PlayerCamera>.Instance.transform.position;
        _throwDirection = (target - _projectile.transform.position).normalized;

        float step = _returnSpeed * Time.deltaTime;
        Vector3 prevPos = _projectile.transform.position;

        _projectile.transform.position += _throwDirection * step;
        _projectile.transform.Rotate(Vector3.forward, SpinSpeed * Time.deltaTime, Space.Self);

        if (step > 0.001f && Physics.SphereCast(prevPos, ThrowHitRadius, _throwDirection, out var hit, step,
                NetworkSingleton<CombatManager>.Instance.MeleeLayerMask, QueryTriggerInteraction.Collide))
        {
            ExecuteThrowHit(hit);
            return;
        }

        if (Vector3.Distance(_projectile.transform.position, target) < ReturnCatchDistance)
        {
            CatchHammer();
        }
    }

    private void ExecuteThrowHit(RaycastHit hit)
    {
        var player = Player.Local;
        if (player != null && (hit.collider.transform.IsChildOf(player.transform) || hit.collider.transform == player.transform))
            return;
        var damageable = hit.collider.GetComponentInParent<IDamageable>();
        float throwDamage = _chargeTimeRemaining > 0f ? Core.ThrowDamage * Core.ChargedThrowMultiplier : Core.ThrowDamage;
        if (damageable != null)
        {
            var impact = new Impact(
                hit.point,
                _throwDirection,
                Core.ThrowForce, throwDamage,
                EImpactType.BluntMetal,
                Player.Local.NetworkObject,
                UnityEngine.Random.Range(int.MinValue, int.MaxValue));

            damageable.SendImpact(impact);
            Singleton<FXManager>.Instance.CreateImpactFX(impact, damageable);
        }

        if (_chargeTimeRemaining > 0f)
        {
            var npc = hit.collider.GetComponentInParent<NPC>();
            if (npc != null)
            {
                Electrifying.ApplyToAvatar(npc.Avatar);
                MelonCoroutines.Start(LightningHelper.ClearElectrifyCoroutine(npc));
                Vector3 hammerPos = _projectile != null
                    ? _projectile.transform.position - _throwDirection * 0.3f
                    : hit.point - _throwDirection * 0.3f;
                LightningHelper.ShootBoltFromTo(hammerPos, hit.point);
                LightningHelper.StrikeLightning(hit.point + Vector3.up * 1.5f);
                PlayThunderSound(hit.point);
                if (Player.Local != null)
                    PlayThunderSound(Player.Local.transform.position + Vector3.up * 0.5f);
                EmitLightningNoise(hit.point);
            }
        }

        PlayerSingleton<PlayerCamera>.Instance.StartCameraShake(0.2f, 0.2f);
    }

    private void CatchHammer()
    {
        if (_projectile != null)
        {
            UnityEngine.Object.Destroy(_projectile);
            _projectile = null;
        }

        SetChargedModel(_chargeTimeRemaining > 0f);

        _state = HammerState.Idle;
    }

    private void CleanupThrow()
    {
        if (_projectile != null)
        {
            UnityEngine.Object.Destroy(_projectile);
            _projectile = null;
        }

        if (_fovOverridden)
        {
            PlayerSingleton<PlayerCamera>.Instance.StopFOVOverride(0f);
            _fovOverridden = false;
        }

        SetChargedModel(_chargeTimeRemaining > 0f);
        if (_hammerModel != null)
            _hammerModel.transform.localRotation = _modelBaseRotation;
        if (_chargedModel != null)
            _chargedModel.transform.localRotation = _modelBaseRotation;

        _state = HammerState.Idle;
    }

    // ════════════════════════════════════════════
    //  FLIGHT (space during charged wind-up)
    // ════════════════════════════════════════════

    private void StartFlight()
    {
        if (_fovOverridden)
        {
            PlayerSingleton<PlayerCamera>.Instance.StopFOVOverride(0f);
            _fovOverridden = false;
        }

        _state = HammerState.Flying;
        _flightStartTime = Time.time;
        _flightShakePhase = 0f;

        var flyModel = ActiveHammerModel ?? _hammerModel;
        if (flyModel != null)
            flyModel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // Flight duration = wind-up time × 1.5 (1s = 1.5s, 5s = 7.5s)
        _flightMomentumTotal = _windUpElapsed * Core.FlightMomentumMultiplier;
        _flightMomentumRemaining = _flightMomentumTotal;

        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        _flightVelocity = cam.forward * Core.FlightSpeed * 1.5f; // 150% at start
        _fovOverridden = true;
        _flightRoll = 0f;
        float initialFov = Singleton<Settings>.Instance.CameraFOV + FlightFOVBoost * 1.5f;
        PlayerSingleton<PlayerCamera>.Instance.OverrideFOV(initialFov, 0.05f);

        _savedGravityMultiplier = PlayerMovement.GravityMultiplier;
        PlayerMovement.GravityMultiplier = 0f;

        // Initial upward push to leave the ground
        PlayerSingleton<PlayerMovement>.Instance.Controller.Move(Vector3.up * 0.5f);
    }

    private void UpdateFlying()
    {
        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        var pm = PlayerSingleton<PlayerMovement>.Instance;

        _flightMomentumRemaining -= Time.deltaTime;

        _flightShakePhase += Time.deltaTime * 10f;

        var flyModel = ActiveHammerModel ?? _hammerModel;
        if (flyModel != null)
        {
            Quaternion baseForward = Quaternion.Euler(90f, 0f, 0f);
            Quaternion wobble = Quaternion.Euler(
                Mathf.Sin(_flightShakePhase) * FlightShakeAmount,
                Mathf.Sin(_flightShakePhase * 1.1f + 1f) * FlightShakeAmount,
                0f);
            flyModel.transform.localRotation = baseForward * wobble;
        }

        // Speed: 150% at start, 100% halfway, 50% at end
        float momentumRatio = _flightMomentumTotal > 0f
            ? Mathf.Clamp01(_flightMomentumRemaining / _flightMomentumTotal)
            : 0f;
        float speedMultiplier = 0.5f + momentumRatio;
        float currentSpeed = Core.FlightSpeed * speedMultiplier;

        // FOV scales with momentum: 150% boost at start → 100% halfway → 50% at end
        float fovBoost = FlightFOVBoost * speedMultiplier;
        float flightFov = Singleton<Settings>.Instance.CameraFOV + fovBoost;
        PlayerSingleton<PlayerCamera>.Instance.OverrideFOV(flightFov, 0.05f);

        Vector3 targetDir = cam.forward;
        _flightVelocity = Vector3.MoveTowards(_flightVelocity, targetDir * currentSpeed, currentSpeed * 2f * Time.deltaTime);

        pm.Controller.Move(_flightVelocity * Time.deltaTime);

        // Banking roll: tilt camera based on lateral movement (inverted so right turn = right wing down)
        float lateral = Vector3.Dot(_flightVelocity, cam.right);
        float targetRoll = Mathf.Clamp(lateral * FlightMaxRoll / (Core.FlightSpeed * 1.5f), -FlightMaxRoll, FlightMaxRoll);
        _flightRoll = Mathf.MoveTowards(_flightRoll, targetRoll, FlightRollSpeed * FlightMaxRoll * Time.deltaTime);
        ApplyFlightRoll(_flightRoll);

        // Camera screenshake during flight: scale with actual velocity, not momentum (ramps down when turning/decelerating)
        float actualSpeedRatio = _flightVelocity.magnitude / (Core.FlightSpeed * 1.5f);
        PlayerSingleton<PlayerCamera>.Instance.StartCameraShake(
            FlightShakeIntensity * Mathf.Clamp01(actualSpeedRatio), 0.1f);

        bool graceExpired = Time.time - _flightStartTime > FlightGracePeriod;
        bool isLanding = graceExpired && pm.IsGrounded;
        if (_flightMomentumRemaining <= 0f || !GameInput.GetButton(GameInput.ButtonCode.Jump) || isLanding)
        {
            StopFlight(isLanding);
        }
    }

    private void ApplyFlightRoll(float roll)
    {
        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        var euler = cam.localEulerAngles;
        cam.localEulerAngles = new Vector3(euler.x, euler.y, roll);
    }

    private void ApplyFlightCameraShake()
    {
        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        float actualSpeedRatio = _flightVelocity.magnitude / (Core.FlightSpeed * 1.5f);
        float intensity = Mathf.Clamp01(actualSpeedRatio) * FlightCamShakePos;
        float rotIntensity = Mathf.Clamp01(actualSpeedRatio) * FlightCamShakeRot;
        float t = _flightShakePhase;
        float px = Mathf.Sin(t * 7.3f) * Mathf.Sin(t * 4.1f) * intensity;
        float py = Mathf.Sin(t * 5.7f + 1f) * Mathf.Sin(t * 3.3f) * intensity;
        float rx = Mathf.Sin(t * 6.2f) * rotIntensity;
        float ry = Mathf.Sin(t * 4.8f + 2f) * rotIntensity;
        cam.localPosition += new Vector3(px, py, 0f);
        cam.localEulerAngles += new Vector3(rx, ry, 0f);
    }

    private void StopFlight(bool isLandingOnGround)
    {
        float landingSpeed = _flightVelocity.magnitude;
        var playerPos = Player.Local != null ? Player.Local.transform.position : Vector3.zero;

        // Ground slam only when actually touching the ground
        if (isLandingOnGround)
        {
            if (landingSpeed > FlightImpactShakeMinSpeed)
            {
                float t = Mathf.Clamp01((landingSpeed - FlightImpactShakeMinSpeed) / 50f);
                float shakeAmount = Mathf.Min(t * FlightImpactShakeMaxAmount, FlightImpactShakeCap);
                float shakeDuration = Mathf.Min(0.1f + t * 0.35f, 0.6f);
                PlayerSingleton<PlayerCamera>.Instance.StartCameraShake(shakeAmount, shakeDuration);
            }
            if (landingSpeed >= Core.FlightImpactMinSpeed)
            {
                float slamRadius = ComputeGroundSlamRadius(landingSpeed);
                PlayGroundSlamEffects(playerPos, landingSpeed, slamRadius);
                ExecuteGroundSlam(playerPos, landingSpeed, slamRadius);
            }
        }

        // Smooth roll recovery instead of snap
        _flightRollRecoveryRemaining = Mathf.Abs(_flightRoll) > 0.5f ? FlightRollRecoveryDuration : 0f;
        if (_flightRollRecoveryRemaining <= 0f)
        {
            _flightRoll = 0f;
            ApplyFlightRoll(0f);
        }

        _state = HammerState.Idle;
        PlayerMovement.GravityMultiplier = _savedGravityMultiplier;
        PlayerSingleton<PlayerMovement>.Instance.CanJump = true;

        if (_fovOverridden)
        {
            PlayerSingleton<PlayerCamera>.Instance.StopFOVOverride(FlightFOVDuration);
            _fovOverridden = false;
        }

        if (_hammerModel != null)
            _hammerModel.transform.localRotation = _modelBaseRotation;
        if (_chargedModel != null)
            _chargedModel.transform.localRotation = _modelBaseRotation;
    }

    private static float ComputeGroundSlamRadius(float landingSpeed)
    {
        float speedRatio = landingSpeed / Core.FlightImpactMinSpeed;
        return Mathf.Clamp(FlightImpactBaseRadius * speedRatio * Core.FlightImpactMultiplier, 2f, 50f);
    }

    private void PlayGroundSlamEffects(Vector3 position, float landingSpeed, float slamRadius)
    {
        PlayExplosionSound(position, landingSpeed);
        Vector3 groundPos = position + Vector3.down * 0.9f;
        ExplosionHelper.PlayGroundImpactVFX(groundPos, landingSpeed, Core.FlightImpactMultiplier, slamRadius);
    }

    private void PlayExplosionSound(Vector3 position, float landingSpeed)
    {
        if (!_explosionClipSearched)
        {
            _explosionClipSearched = true;
            try
            {
                var allSources = Resources.FindObjectsOfTypeAll<AudioSource>();
                foreach (var src in allSources)
                {
                    if (src == null || src.clip == null) continue;
                    var go = src.gameObject;
                    if (go.name.Equals("Close expl", StringComparison.OrdinalIgnoreCase))
                    {
                        var parent = go.transform.parent;
                        if (parent != null && parent.name.IndexOf("Explosion sounds", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _explosionClip = src.clip;
                            Melon<Core>.Logger.Msg($"[Explosion sound] Found via '{go.name}' under {parent.name}");
                            break;
                        }
                    }
                }
                if (_explosionClip == null)
                {
                    var allGos = Resources.FindObjectsOfTypeAll<GameObject>();
                    foreach (var go in allGos)
                    {
                        if (go == null) continue;
                        if (go.name.Equals("Close expl", StringComparison.OrdinalIgnoreCase))
                        {
                            var parent = go.transform.parent;
                            if (parent != null && parent.name.IndexOf("Explosion sounds", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var src = go.GetComponent<AudioSource>();
                                if (src != null && src.clip != null)
                                {
                                    _explosionClip = src.clip;
                                    Melon<Core>.Logger.Msg($"[Explosion sound] Found via GameObject '{go.name}'");
                                    break;
                                }
                            }
                        }
                    }
                }
                if (_explosionClip == null)
                    Melon<Core>.Logger.Warning("[Explosion sound] Close expl not found. Visit Manor to load explosion assets.");
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Warning($"[Explosion sound] Search failed: {ex.Message}");
            }
        }
        if (_explosionClip != null)
        {
            float minS = Core.FlightImpactMinSpeed;
            float spd = Mathf.Max(landingSpeed, minS);
            // +1% volume per 1 m/s above min (capped), then round to 1% steps
            float volume = Mathf.Clamp(0.1f + (spd - minS) * 0.01f, 0.1f, 1f) * Core.FlightImpactMultiplier;
            volume = Mathf.Round(volume * 100f) / 100f;
            float pitch = Mathf.Clamp(0.82f + (spd - minS) * 0.01f, 0.82f, 1.22f);
            pitch = Mathf.Round(pitch * 100f) / 100f;
            PlayExplosionOneShot(_explosionClip, position, Mathf.Clamp01(volume), pitch);
        }
    }

    private static void PlayExplosionOneShot(AudioClip clip, Vector3 position, float volume, float pitch)
    {
        var go = new GameObject("ThorSlamExplosionAudio");
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        src.spatialBlend = 1f;
        src.dopplerLevel = 0f;
        src.Play();
        float life = clip.length / Mathf.Max(0.05f, src.pitch) + 0.08f;
        UnityEngine.Object.Destroy(go, life);
    }

    private void ExecuteGroundSlam(Vector3 center, float landingSpeed, float radius)
    {
        float speedRatio = landingSpeed / Core.FlightImpactMinSpeed;
        float baseDamage = landingSpeed * 0.8f * Core.FlightImpactMultiplier;
        float forceMultiplier = Mathf.Pow(speedRatio, 1.5f) * Core.FlightImpactMultiplier;
        float baseForce = 80f * forceMultiplier;

        var hits = Physics.OverlapSphere(center, radius,
            NetworkSingleton<CombatManager>.Instance.MeleeLayerMask, QueryTriggerInteraction.Collide);
        var player = Player.Local;
        foreach (var col in hits)
        {
            if (player != null && (col.transform.IsChildOf(player.transform) || col.transform == player.transform))
                continue; // Exclude player from ground slam damage
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;
            float dist = Vector3.Distance(center, col.ClosestPoint(center));
            float falloff = 1f - Mathf.Clamp01(dist / radius) * 0.85f; // 15–100% falloff
            float damage = baseDamage * falloff;
            float force = baseForce * falloff;
            if (damage < 1f && force < 10f) continue;
            var dir = (col.transform.position - center).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.down;
            var impact = new Impact(
                col.ClosestPoint(center),
                dir, force, damage,
                EImpactType.BluntMetal,
                Player.Local.NetworkObject,
                UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            damageable.SendImpact(impact);
            Singleton<FXManager>.Instance.CreateImpactFX(impact, damageable);
        }
    }

    // ════════════════════════════════════════════
    //  CHARGE (hold key: 3s rise + shake, weather at 2s, model swap at 3s)
    // ════════════════════════════════════════════

    private void StartChargeHold()
    {
        _state = HammerState.Charging;
        _chargeHoldElapsed = 0f;
    }

    private void UpdateCharging()
    {
        if (!Input.GetKey(Core.LightningKey))
        {
            CancelChargeHold();
            return;
        }

        _chargeHoldElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_chargeHoldElapsed / Core.ChargeHoldDuration);
        var chargeModel = _hammerModel;
        if (chargeModel == null)
        {
            CancelChargeHold();
            return;
        }

        // Hammer rises (y from -0.15 to 0.1), time-based so FPS-independent
        float riseY = Mathf.Lerp(_hammerModelBasePos.y, 0.1f, t);
        chargeModel.transform.localPosition = new Vector3(_hammerModelBasePos.x, riseY, _hammerModelBasePos.z);

        // Shake: deterministic, time-based (no Random per frame), reduced intensity (±0.3° to ±1.2°)
        float shakeAmount = Mathf.Lerp(0.3f, 1.2f, t);
        float time = _chargeHoldElapsed * 12f;
        chargeModel.transform.localRotation = _modelBaseRotation * Quaternion.Euler(
            Mathf.Sin(time) * shakeAmount,
            Mathf.Sin(time * 1.3f + 1f) * shakeAmount,
            Mathf.Sin(time * 0.9f + 2f) * shakeAmount);

        // Complete at 3s — lightning when animation finishes (no heavy rain needed)
        if (_chargeHoldElapsed >= Core.ChargeHoldDuration)
        {
            CompleteChargeHold();
        }
    }

    private void CancelChargeHold()
    {
        _state = HammerState.Idle;
        if (_hammerModel != null)
        {
            _hammerModel.transform.localPosition = _hammerModelBasePos;
            _hammerModel.transform.localRotation = _modelBaseRotation;
        }
    }

    private void CompleteChargeHold()
    {
        _state = HammerState.Idle;
        _chargeTimeRemaining = Core.ChargeDuration;
        SetChargedModel(true);

        if (_hammerModel != null)
        {
            _hammerModel.transform.localPosition = _hammerModelBasePos;
            _hammerModel.transform.localRotation = _modelBaseRotation;
        }
        if (_chargedModel != null)
            _chargedModel.transform.localPosition = new Vector3(_hammerModelBasePos.x, 0.1f, _hammerModelBasePos.z);

        _chargeSettleElapsed = ChargeSettleDelay + ChargeSettleDuration;
        _hammerShakeElapsed = 1.5f;
        TriggerHammerChargeLightningBurst();

        if (Player.Local != null)
        {
            var pos = Player.Local.transform.position;
            StrikeLightningOnPlayer(pos);
            PlayThunderSound(pos + Vector3.down * 1f);
        }
    }

    private void TriggerHammerChargeLightningBurst()
    {
        var model = ActiveHammerModel ?? _chargedModel ?? _hammerModel;
        if (model == null) return;
        MelonCoroutines.Start(ChargeHammerLightningBurst(model.transform));
    }

    private IEnumerator ChargeHammerLightningBurst(Transform hammer)
    {
        if (hammer == null) yield break;
        var cam = PlayerSingleton<PlayerCamera>.Instance;
        var camT = cam != null ? cam.transform : null;

        for (int i = 0; i < 4; i++)
        {
            if (hammer == null) yield break;
            Vector3 target = hammer.position + new Vector3(
                UnityEngine.Random.Range(-0.03f, 0.03f),
                UnityEngine.Random.Range(-0.02f, 0.04f),
                UnityEngine.Random.Range(-0.03f, 0.03f));
            Vector3 from;
            if (camT != null)
            {
                // Spawn bolt sources around/above the first-person hammer so they visibly converge into it.
                float yaw = UnityEngine.Random.Range(-70f, 70f);
                Vector3 lateral = Quaternion.Euler(0f, yaw, 0f) * camT.right;
                from = target + camT.up * UnityEngine.Random.Range(0.75f, 1.25f) + lateral * UnityEngine.Random.Range(0.18f, 0.4f);
            }
            else
            {
                from = target + Vector3.up * UnityEngine.Random.Range(0.75f, 1.25f) +
                       UnityEngine.Random.insideUnitSphere * 0.35f;
            }
            LightningHelper.ShootBoltFromTo(from, target);

            if (i < 3)
                yield return new WaitForSeconds(0.05f);
        }
    }

    private static void StrikeLightningOnPlayer(Vector3 playerPos)
    {
        LightningHelper.StrikeLightning(playerPos + Vector3.up * 1.5f);
    }

    private void TryLightningZapToTarget()
    {
        var cam = PlayerSingleton<PlayerCamera>.Instance;
        Vector3 targetPoint;
        if (cam.LookRaycast(Core.LightningZapRange, out var hit,
                NetworkSingleton<CombatManager>.Instance.MeleeLayerMask,
                includeTriggers: true, LightningAimRadius))
            targetPoint = hit.point;
        else
            targetPoint = cam.transform.position + cam.transform.forward * Core.LightningZapRange;

        var npc = hit.collider != null ? hit.collider.GetComponentInParent<NPC>() : null;
        if (npc != null)
        {
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                var impact = new Impact(
                    hit.point,
                    cam.transform.forward,
                    Core.LightningForce, Core.LightningDamage,
                    EImpactType.BluntMetal,
                    Player.Local.NetworkObject,
                    UnityEngine.Random.Range(int.MinValue, int.MaxValue));
                damageable.SendImpact(impact);
            }
            Electrifying.ApplyToAvatar(npc.Avatar);
            MelonCoroutines.Start(LightningHelper.ClearElectrifyCoroutine(npc));
        }

        Vector3 hammerPos = (ActiveHammerModel ?? _hammerModel) != null
            ? (ActiveHammerModel ?? _hammerModel).transform.position
            : cam.transform.position + cam.transform.forward * 0.6f;
        LightningHelper.ShootBoltFromTo(hammerPos, targetPoint);
        PlayThunderSound(targetPoint);
        if (Player.Local != null)
            PlayThunderSound(Player.Local.transform.position + Vector3.up * 0.5f);
        EmitLightningNoise(targetPoint);
        PlayerSingleton<PlayerCamera>.Instance.StartCameraShake(0.2f, 0.2f);
    }

    private static void EmitLightningNoise(Vector3 position)
    {
        if (Core.LightningPanicRadius > 0f && Player.Local != null)
            NoiseUtility.EmitNoise(position, ENoiseType.Explosion, Core.LightningPanicRadius, Player.Local.gameObject);
    }

    private void PlayThunderSound(Vector3 position)
    {
        if (!_thunderClipSearched)
        {
            _thunderClipSearched = true;
            var all = Resources.FindObjectsOfTypeAll<ThunderController>();
            ThunderController tc = all.Length > 0 ? all[0] : null;
            if (tc != null)
            {
                try
                {
#if IL2CPP
                    var audio = tc._lightningAudio;
                    if (audio != null && audio.Clips != null && audio.Clips.Count > 0)
                        _thunderClip = audio.Clips[0];
#else
                    var field = typeof(ThunderController).GetField("_lightningAudio",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field?.GetValue(tc) is AudioSourceController asc)
                    {
                        var clipsField = asc.GetType().GetField("Clips",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (clipsField?.GetValue(asc) is AudioClip[] clips && clips.Length > 0)
                            _thunderClip = clips[0];
                    }
#endif
                }
                catch { }
            }
            if (_thunderClip == null)
            {
                try
                {
                    var allClips = Resources.FindObjectsOfTypeAll<AudioClip>();
                    foreach (var clip in allClips)
                    {
                        if (clip == null) continue;
                        var name = clip.name.ToLowerInvariant();
                        if (name.Contains("thunder") || name.Contains("lightning"))
                        {
                            _thunderClip = clip;
                            break;
                        }
                    }
                }
                catch { }
            }
        }

        if (_thunderClip != null)
            AudioSource.PlayClipAtPoint(_thunderClip, position, 1f);
    }

    private void PlayPunchSound(Vector3 position)
    {
        if (!_punchClipSearched)
        {
            _punchClipSearched = true;
            try
            {
                // Find like thunder: by GameObject name "PunchController" (child AudioSource) or "Punch sound"
                var allSources = Resources.FindObjectsOfTypeAll<AudioSource>();
                foreach (var src in allSources)
                {
                    if (src == null || src.clip == null) continue;
                    var go = src.gameObject;
                    if (go.name.Equals("Punch sound", StringComparison.OrdinalIgnoreCase))
                    {
                        _punchClip = src.clip;
                        Melon<Core>.Logger.Msg($"[Punch sound] Found via GameObject '{go.name}' (clip: {src.clip?.name})");
                        break;
                    }
                    if (go.transform.parent != null && go.transform.parent.name.Equals("PunchController", StringComparison.OrdinalIgnoreCase))
                    {
                        _punchClip = src.clip;
                        Melon<Core>.Logger.Msg($"[Punch sound] Found via PunchController child '{go.name}' (clip: {src.clip?.name})");
                        break;
                    }
                }
                if (_punchClip == null)
                {
                    Melon<Core>.Logger.Msg("[Punch sound] Not found via AudioSource, searching GameObjects...");
                    var allGos = Resources.FindObjectsOfTypeAll<GameObject>();
                    int logged = 0;
                    foreach (var go in allGos)
                    {
                        if (go == null) continue;
                        if (go.name.Equals("Punch sound", StringComparison.OrdinalIgnoreCase))
                        {
                            var src = go.GetComponent<AudioSource>();
                            if (src != null && src.clip != null)
                            {
                                _punchClip = src.clip;
                                Melon<Core>.Logger.Msg($"[Punch sound] Found via GameObject '{go.name}'");
                                break;
                            }
                        }
                        if (go.name.Equals("PunchController", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (Transform child in go.transform)
                            {
                                var src = child.GetComponent<AudioSource>();
                                if (src != null && src.clip != null)
                                {
                                    _punchClip = src.clip;
                                    Melon<Core>.Logger.Msg($"[Punch sound] Found via PunchController child '{child.name}'");
                                    break;
                                }
                            }
                            if (_punchClip != null) break;
                        }
                        if (logged < 5 && (go.name.IndexOf("Punch", StringComparison.OrdinalIgnoreCase) >= 0 || go.name.IndexOf("punch", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            Melon<Core>.Logger.Msg($"[Punch sound] Found potential: GameObject '{go.name}' (parent: {go.transform.parent?.name ?? "null"})");
                            logged++;
                        }
                    }
                }
                if (_punchClip == null)
                {
                    var allClips = Resources.FindObjectsOfTypeAll<AudioClip>();
                    foreach (var clip in allClips)
                    {
                        if (clip == null) continue;
                        if (clip.name.IndexOf("punch", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _punchClip = clip;
                            Melon<Core>.Logger.Msg($"[Punch sound] Found via AudioClip name '{clip.name}'");
                            break;
                        }
                    }
                }
                if (_punchClip == null)
                    Melon<Core>.Logger.Warning("[Punch sound] No clip found. Try attacking an NPC first to load combat assets.");
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Warning($"[Punch sound] Search failed: {ex.Message}");
            }
        }
        if (_punchClip != null)
        {
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam != null && cam.gameObject != null)
            {
                var oneShot = cam.gameObject.AddComponent<AudioSource>();
                oneShot.spatialBlend = 0f;
                oneShot.clip = _punchClip;
                oneShot.volume = 1f;
                oneShot.Play();
                MelonCoroutines.Start(DestroyAudioSourceAfterClip(oneShot, _punchClip.length));
            }
            else
            {
                AudioSource.PlayClipAtPoint(_punchClip, position, 1f);
            }
        }
    }

    private static IEnumerator DestroyAudioSourceAfterClip(AudioSource src, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (src != null)
            UnityEngine.Object.Destroy(src);
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }
}
