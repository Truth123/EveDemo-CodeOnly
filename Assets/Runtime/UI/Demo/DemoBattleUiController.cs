// 文件说明：维护 Demo 标题、战斗 HUD、静态战斗操作提示、暂停菜单、胜负结算、战斗背景音乐和 UI 资源显示。
// 所属模块：战斗 UI。
// 运行影响：影响演示入口、标题补光、开场镜头、战斗背景音乐、操作说明显隐、锁定目标提示、战斗计时、资源条显示和调试交互。

using System.Collections;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Actor;
using ProjectEVE.CameraSystem.LockOn;
using ProjectEVE.CameraSystem.Rigs;
using ProjectEVE.Combat;
using ProjectEVE.Core.Debugging;
using ProjectEVE.Feedback;
using ProjectEVE.Input;
using ProjectEVE.Player;
using ProjectEVE.UI.HUD;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectEVE.UI.Demo
{
    /// <summary>
    /// Demo 场景展示总控。运行时创建标题、战斗 HUD、暂停菜单和胜负结算，并管理展示阶段的输入、镜头与背景音乐。
    /// </summary>
    public sealed class DemoBattleUiController : MonoBehaviour
    {
        private enum DemoPresentationState
        {
            Title,
            OpeningTransition,
            Combat,
            PendingVictory,
            PendingDefeat,
            Result
        }

        [Header("References")]
        [SerializeField] private PlayerStateMachine playerStateMachine;
        [SerializeField] private PlayerInputReader playerInputReader;
        [SerializeField] private PlayerCameraRig playerCameraRig;
        [SerializeField] private BossActor bossActor;
        [SerializeField] private CombatResourceComponent bossResource;
        [SerializeField] private CombatHitStop hitStop;
        [SerializeField] private EveDebugOverlay debugOverlay;
        [SerializeField] private CombatHudOverlay legacyHudOverlay;
        [SerializeField] private Transform titleCameraAnchor;
        [SerializeField] private Light titleCharacterLight;
        [SerializeField] private ParticleSystem lockOnIndicatorWorldMarker;
        [SerializeField] private AudioSource backgroundMusicSource;

        [Header("Presentation Assets")]
        [SerializeField] private Font presentationFont;
        [SerializeField] private Font battleTimerFont;
        [SerializeField] private Sprite presentationGradient;
        [SerializeField] private Sprite presentationScanlines;

        [Header("Background Music")]
        [SerializeField, Range(0f, 1f)] private float backgroundMusicVolume = 0.22f;

        [Header("Startup")]
        [SerializeField] private bool showHudOnStart = true;
        [FormerlySerializedAs("showTrainingPromptOnStart")]
        [SerializeField] private bool showDebugOnStart;
        [SerializeField] private bool showDetailedDebugOverlay;
        [SerializeField] private string bossDisplayName = ":: Raven ::";

        [Header("Presentation Timing")]
        [SerializeField] private float titleUiFadeDuration = 0.35f;
        [SerializeField] private float openingCameraTransitionDuration = 2.4f;
        [SerializeField] private float resultRevealDelay = 4.8f;
        [SerializeField] private float resultFadeDuration = 0.35f;
        [SerializeField] private float titleCameraFieldOfView = 38f;

        private CanvasGroup hudGroup;
        private CanvasGroup debugGroup;
        private CanvasGroup pauseGroup;
        private CanvasGroup titleGroup;
        private CanvasGroup resultGroup;
        private CanvasGroup transitionGroup;
        private RectTransform controlGuideRoot;
        private Text bossStatusText;
        private Text beValueText;
        private Text hpValueText;
        private Text bossHpValueText;
        private Text bossShieldValueText;
        private Text bossAiDebugText;
        private Text resultTitleText;
        private Text resultSubtitleText;
        private Text resultTimeText;
        private Button titleStartButton;
        private Button resultRetryButton;
        private SegmentedBarView bossHpBar;
        private SegmentedBarView bossShieldBar;
        private SegmentedBarView beBar;
        private SegmentedBarView hpBar;
        private Font uiFont;
        private bool debugVisible;
        private bool paused;
        private bool transitionRunning;
        private bool resultIsVictory;
        private float restoreFixedDeltaTime;
        private float battleElapsedUnscaled;
        private float pendingResultElapsedUnscaled;
        private DemoPresentationState presentationState;

        private UnityEngine.Camera presentationCamera;
        private Vector3 gameplayCameraPosition;
        private Quaternion gameplayCameraRotation;
        private float gameplayCameraFieldOfView;
        private bool playerInputWasEnabled;
        private bool playerCameraRigWasEnabled;
        private bool titleCharacterLightWasEnabled;
        private bool dependenciesCaptured;
        private CursorLockMode originalCursorLockMode;
        private bool originalCursorVisible;

        private static bool startCombatAfterReload;

        private static readonly Color White = new Color(0.92f, 0.95f, 0.96f, 0.96f);
        private static readonly Color Empty = new Color(0.12f, 0.16f, 0.18f, 0.62f);
        private static readonly Color Cyan = new Color(0.34f, 0.86f, 0.92f, 0.95f);
        private static readonly Color GuardBlue = new Color(0.72f, 0.93f, 0.96f, 0.95f);
        private static readonly Color DimPanel = new Color(0f, 0f, 0f, 0.42f);
        private static readonly Color PresentationCyan = new Color(0.22f, 0.9f, 1f, 1f);
        private static readonly Color PresentationWhite = new Color(0.9f, 0.93f, 0.95f, 1f);

        /// <summary>
        /// 在进入新的 Player 生命周期时清除一次性重试意图，避免关闭 Domain Reload 后污染下一次 Play。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticPresentationIntent()
        {
            startCombatAfterReload = false;
        }

        private void Awake()
        {
            restoreFixedDeltaTime = Time.fixedDeltaTime;
            BindReferences();
            CapturePresentationDependencies();
            uiFont = presentationFont != null ? presentationFont : ResolveFont();
            BuildUi();
            SetPaused(false, false);
            legacyHudOverlay?.SetVisible(false);

            bool skipTitle = startCombatAfterReload;
            startCombatAfterReload = false;
            if (skipTitle)
            {
                EnterCombatImmediately();
            }
            else
            {
                EnterTitle();
            }
        }

        private void Update()
        {
            BindReferences();
            TickPresentation();
            HandlePauseInput();
            HandleControlGuideInput();
            RefreshHud();
        }

        private void LateUpdate()
        {
            UpdateLockOnIndicator();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            hitStop?.CancelAllTimeDilation();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = restoreFixedDeltaTime;
            paused = false;
            transitionRunning = false;
            SetLockOnIndicatorVisible(false);
            StopBackgroundMusic();

            if (dependenciesCaptured)
            {
                if (playerInputReader != null)
                {
                    playerInputReader.enabled = playerInputWasEnabled;
                }

                RestoreGameplayCamera(playerCameraRigWasEnabled);
                if (titleCharacterLight != null)
                {
                    titleCharacterLight.enabled = titleCharacterLightWasEnabled;
                }
                Cursor.lockState = originalCursorLockMode;
                Cursor.visible = originalCursorVisible;
            }
        }

        /// <summary>
        /// 在场景全部组件完成启用后重新落实标题页的输入与相机约束，避免同帧 OnEnable 顺序恢复战斗控制。
        /// </summary>
        private void Start()
        {
            if (presentationState == DemoPresentationState.Title)
            {
                EnterTitle();
                return;
            }

            if (presentationState == DemoPresentationState.OpeningTransition)
            {
                EnterTitle();
            }
        }

        /// <summary>
        /// 在编辑器 Game View 重建或组件重新启用后延迟一帧恢复展示约束，避免 OnDisable 的安全还原泄漏到标题或结算状态。
        /// </summary>
        private void OnEnable()
        {
            StartCoroutine(ReapplyPresentationAfterEnable());
        }

        /// <summary>
        /// 等待同帧组件启用完成后，根据当前展示状态重新施加输入、相机、时间和光标约束。
        /// </summary>
        /// <returns>延迟到下一帧执行状态恢复的 Unity 协程枚举器。</returns>
        private IEnumerator ReapplyPresentationAfterEnable()
        {
            yield return null;
            if (!dependenciesCaptured)
            {
                yield break;
            }

            if (presentationState == DemoPresentationState.Title ||
                presentationState == DemoPresentationState.OpeningTransition)
            {
                EnterTitle();
                yield break;
            }

            if (presentationState == DemoPresentationState.PendingVictory ||
                presentationState == DemoPresentationState.PendingDefeat)
            {
                StopBackgroundMusic();
                SetCombatInputEnabled(false);
                Time.timeScale = 1f;
                Time.fixedDeltaTime = restoreFixedDeltaTime;
                yield break;
            }

            if (presentationState == DemoPresentationState.Result)
            {
                StopBackgroundMusic();
                SetCombatInputEnabled(false);
                Time.timeScale = 0f;
                Time.fixedDeltaTime = restoreFixedDeltaTime;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                yield break;
            }

            if (presentationState == DemoPresentationState.Combat)
            {
                PlayBackgroundMusic();
            }
        }

        /// <summary>
        /// 记录标题界面会临时覆盖的输入、相机和光标状态，使组件禁用或场景卸载时可以完整恢复。
        /// </summary>
        private void CapturePresentationDependencies()
        {
            presentationCamera = playerCameraRig != null
                ? playerCameraRig.GetComponent<UnityEngine.Camera>()
                : UnityEngine.Camera.main;
            if (presentationCamera != null)
            {
                gameplayCameraPosition = presentationCamera.transform.position;
                gameplayCameraRotation = presentationCamera.transform.rotation;
                gameplayCameraFieldOfView = presentationCamera.fieldOfView;
            }

            playerInputWasEnabled = playerInputReader != null && playerInputReader.enabled;
            playerCameraRigWasEnabled = playerCameraRig != null && playerCameraRig.enabled;
            titleCharacterLightWasEnabled = titleCharacterLight != null && titleCharacterLight.enabled;
            originalCursorLockMode = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            dependenciesCaptured = true;
        }

        /// <summary>
        /// 推进战斗计时和死亡结果判定；结果首次确定后冻结计时并进入死亡动画等待阶段。
        /// </summary>
        private void TickPresentation()
        {
            if (presentationState == DemoPresentationState.Combat)
            {
                DemoPresentationState deathState = ResolveDeathPresentationState(
                    playerStateMachine != null && playerStateMachine.Context.CurrentState == PlayerStateId.Dead,
                    bossActor != null && bossActor.CurrentState == BossStateId.Dead);
                if (deathState == DemoPresentationState.PendingDefeat)
                {
                    BeginPendingResult(false);
                    return;
                }

                if (deathState == DemoPresentationState.PendingVictory)
                {
                    BeginPendingResult(true);
                    return;
                }

                if (!paused)
                {
                    battleElapsedUnscaled += Time.unscaledDeltaTime;
                }

                return;
            }

            if (presentationState != DemoPresentationState.PendingVictory &&
                presentationState != DemoPresentationState.PendingDefeat)
            {
                return;
            }

            pendingResultElapsedUnscaled += Time.unscaledDeltaTime;
            if (pendingResultElapsedUnscaled >= resultRevealDelay)
            {
                ShowResult();
            }
        }

        /// <summary>
        /// 按“玩家死亡优先”解析同帧胜负，保证双方同时死亡时稳定进入失败结算。
        /// </summary>
        /// <param name="playerDead">玩家是否已经进入正式 Dead 状态。</param>
        /// <param name="bossDead">Boss 是否已经进入正式 Dead 状态。</param>
        /// <returns>需要进入的等待结算状态；双方均未死亡时返回 Combat。</returns>
        private static DemoPresentationState ResolveDeathPresentationState(bool playerDead, bool bossDead)
        {
            if (playerDead)
            {
                return DemoPresentationState.PendingDefeat;
            }

            return bossDead ? DemoPresentationState.PendingVictory : DemoPresentationState.Combat;
        }

        /// <summary>
        /// 进入标题展示，停止战斗输入和 Boss AI，并把游戏相机切换到 Eve 近景锚点。
        /// </summary>
        private void EnterTitle()
        {
            StopBackgroundMusic();
            hitStop?.CancelAllTimeDilation();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = restoreFixedDeltaTime;
            presentationState = DemoPresentationState.Title;
            battleElapsedUnscaled = 0f;
            pendingResultElapsedUnscaled = 0f;
            transitionRunning = false;
            SetPaused(false, false);
            bossActor?.StopBossAi();
            SetCombatInputEnabled(false);
            playerStateMachine?.PrepareTitlePresentation();
            ApplyTitleCamera();
            SetTitleCharacterLight(true);

            SetGroupVisible(titleGroup, true);
            SetGroupVisible(hudGroup, false);
            SetGroupVisible(debugGroup, false);
            SetGroupVisible(pauseGroup, false);
            SetGroupVisible(resultGroup, false);
            SetTransitionVisible(false, 0f);
            debugOverlay?.SetVisible(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (EventSystem.current != null && titleStartButton != null)
            {
                EventSystem.current.SetSelectedGameObject(titleStartButton.gameObject);
            }
        }

        /// <summary>
        /// 响应标题页开始按钮，启动标题近景到锁定战斗机位的可见缓动。
        /// </summary>
        private void StartCombatFromTitle()
        {
            if (presentationState != DemoPresentationState.Title || transitionRunning)
            {
                return;
            }

            StartCoroutine(StartCombatTransition());
        }

        /// <summary>
        /// 使用非缩放时间同时推进标题淡出和开场镜头，镜头到位前不启用任何战斗逻辑。
        /// </summary>
        /// <returns>供 Unity 协程逐帧推进标题淡出与相机插值的枚举器。</returns>
        private IEnumerator StartCombatTransition()
        {
            transitionRunning = true;
            presentationState = DemoPresentationState.OpeningTransition;
            PlayBackgroundMusic();
            bossActor?.StopBossAi();
            SetCombatInputEnabled(false);
            if (titleGroup != null)
            {
                titleGroup.interactable = false;
                titleGroup.blocksRaycasts = true;
            }

            SetTransitionVisible(false, 0f);
            LockOnTarget bossLockTarget = ResolveBossLockOnTarget();
            Vector3 startPosition = presentationCamera != null
                ? presentationCamera.transform.position
                : gameplayCameraPosition;
            Quaternion startRotation = presentationCamera != null
                ? presentationCamera.transform.rotation
                : gameplayCameraRotation;
            float startFieldOfView = presentationCamera != null
                ? presentationCamera.fieldOfView
                : titleCameraFieldOfView;

            Vector3 targetPosition = gameplayCameraPosition;
            Quaternion targetRotation = gameplayCameraRotation;
            if (playerCameraRig != null && bossLockTarget != null)
            {
                playerCameraRig.TryGetLockOnCameraPose(
                    bossLockTarget.TargetPoint,
                    out targetPosition,
                    out targetRotation);
            }

            float duration = Mathf.Max(0f, openingCameraTransitionDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
                float eased = normalized * normalized * (3f - 2f * normalized);
                ApplyOpeningCameraPose(
                    Vector3.LerpUnclamped(startPosition, targetPosition, eased),
                    Quaternion.SlerpUnclamped(startRotation, targetRotation, eased),
                    Mathf.LerpUnclamped(startFieldOfView, gameplayCameraFieldOfView, eased));

                if (titleGroup != null)
                {
                    float titleFade = titleUiFadeDuration > 0f
                        ? Mathf.Clamp01(elapsed / titleUiFadeDuration)
                        : 1f;
                    titleGroup.alpha = 1f - titleFade;
                }

                yield return null;
            }

            ApplyOpeningCameraPose(targetPosition, targetRotation, gameplayCameraFieldOfView);
            SetGroupVisible(titleGroup, false);
            EnterCombatCore(false, bossLockTarget);
            transitionRunning = false;
        }

        /// <summary>
        /// 在“再次挑战”重载完成后直接进入战斗，跳过标题和镜头过渡。
        /// </summary>
        private void EnterCombatImmediately()
        {
            EnterCombatCore(true, ResolveBossLockOnTarget());
            SetTransitionVisible(false, 0f);
        }

        /// <summary>
        /// 恢复正式战斗相机和输入，启动 Boss AI，并显示 HUD 与训练提示。
        /// </summary>
        /// <param name="restoreCapturedCamera">true 时先恢复场景配置的战斗机位；false 时沿用已经到位的开场镜头。</param>
        /// <param name="bossLockTarget">需要在开放输入前自动锁定的 Boss 目标。</param>
        private void EnterCombatCore(bool restoreCapturedCamera, LockOnTarget bossLockTarget)
        {
            hitStop?.CancelAllTimeDilation();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = restoreFixedDeltaTime;
            battleElapsedUnscaled = 0f;
            pendingResultElapsedUnscaled = 0f;
            SetPaused(false, false);
            if (restoreCapturedCamera)
            {
                RestoreGameplayCamera(false);
            }

            bool locked = playerStateMachine != null &&
                playerStateMachine.BeginCombatIdleAndLockOn(bossLockTarget);
            if (playerCameraRig != null)
            {
                playerCameraRig.SynchronizeFromCurrentPose(locked);
                playerCameraRig.enabled = true;
            }

            SetTitleCharacterLight(false);
            bossActor?.StartBossAiWithInitialAttackCooldowns();
            SetCombatInputEnabled(true);
            presentationState = DemoPresentationState.Combat;
            PlayBackgroundMusic();

            SetGroupVisible(titleGroup, false);
            SetGroupVisible(resultGroup, false);
            SetGroupVisible(hudGroup, showHudOnStart);
            SetDebugVisible(showDebugOnStart);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// 首次检测到死亡时停止输入和正式 HUD，并保留约 4.8 秒给现有死亡动画播放。
        /// </summary>
        /// <param name="victory">为 true 时记录 Boss 死亡胜利；为 false 时记录玩家死亡失败。</param>
        private void BeginPendingResult(bool victory)
        {
            StopBackgroundMusic();
            resultIsVictory = victory;
            presentationState = victory
                ? DemoPresentationState.PendingVictory
                : DemoPresentationState.PendingDefeat;
            pendingResultElapsedUnscaled = 0f;
            hitStop?.CancelAllTimeDilation();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = restoreFixedDeltaTime;
            SetCombatInputEnabled(false);
            if (!victory)
            {
                bossActor?.StopBossAi();
            }

            SetGroupVisible(hudGroup, false);
            SetGroupVisible(debugGroup, false);
            SetGroupVisible(pauseGroup, false);
            debugOverlay?.SetVisible(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// 结束死亡动画等待，冻结场景并以结果类型和最终战斗时间淡入结算页。
        /// </summary>
        private void ShowResult()
        {
            if (presentationState == DemoPresentationState.Result)
            {
                return;
            }

            presentationState = DemoPresentationState.Result;
            hitStop?.CancelAllTimeDilation();
            Time.timeScale = 0f;
            Time.fixedDeltaTime = restoreFixedDeltaTime;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            resultTitleText.text = resultIsVictory ? "作战完成" : "作战失败";
            resultSubtitleText.text = resultIsVictory ? "目标已清除：渡鸦" : "伊芙机能停止";
            resultTimeText.text = FormatBattleTime(battleElapsedUnscaled);
            resultGroup.alpha = 0f;
            resultGroup.interactable = false;
            resultGroup.blocksRaycasts = true;
            StartCoroutine(FadeCanvasGroup(resultGroup, 0f, 1f, resultFadeDuration, true));
            if (EventSystem.current != null && resultRetryButton != null)
            {
                EventSystem.current.SetSelectedGameObject(resultRetryButton.gameObject);
            }
        }

        /// <summary>
        /// 把非缩放战斗秒数格式化为结果页使用的 mm:ss.ff 文本。
        /// </summary>
        /// <param name="seconds">从开始战斗到首次死亡判定累计的非缩放秒数。</param>
        /// <returns>钳制到非负值后的两位分钟、秒和百分秒文本。</returns>
        private static string FormatBattleTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int totalCentiseconds = Mathf.FloorToInt(seconds * 100f);
            int minutes = totalCentiseconds / 6000;
            int remainingCentiseconds = totalCentiseconds % 6000;
            int wholeSeconds = remainingCentiseconds / 100;
            int centiseconds = remainingCentiseconds % 100;
            return $"{minutes:00}:{wholeSeconds:00}.{centiseconds:00}";
        }

        /// <summary>
        /// 点击开始进入 OpeningTransition 或重试直接进入 Combat 时，以低音量二维循环模式启动场景背景音乐；重复调用不会从头打断。
        /// </summary>
        private void PlayBackgroundMusic()
        {
            if (backgroundMusicSource == null || backgroundMusicSource.clip == null)
            {
                Debug.LogError(
                    $"{nameof(DemoBattleUiController)} requires a background AudioSource with an AudioClip.",
                    this);
                return;
            }

            backgroundMusicSource.playOnAwake = false;
            backgroundMusicSource.loop = true;
            backgroundMusicSource.spatialBlend = 0f;
            backgroundMusicSource.volume = Mathf.Clamp01(backgroundMusicVolume);
            if (!backgroundMusicSource.isPlaying)
            {
                backgroundMusicSource.Play();
            }
        }

        /// <summary>
        /// 在战斗结束、返回标题、场景重载或组件禁用时立即停止背景音乐并复位播放位置。
        /// </summary>
        private void StopBackgroundMusic()
        {
            if (backgroundMusicSource != null)
            {
                backgroundMusicSource.Stop();
            }
        }

        /// <summary>
        /// 按非缩放时间插值 CanvasGroup Alpha，并在结束时选择是否开放按钮交互。
        /// </summary>
        /// <param name="group">需要淡入或淡出的 UI 组。</param>
        /// <param name="from">起始 Alpha。</param>
        /// <param name="to">结束 Alpha。</param>
        /// <param name="duration">过渡持续秒数，使用非缩放时间。</param>
        /// <param name="enableInteractionAtEnd">结束后是否允许该组接收选择和点击。</param>
        /// <returns>供 Unity 协程逐帧推进透明度的枚举器。</returns>
        private static IEnumerator FadeCanvasGroup(
            CanvasGroup group,
            float from,
            float to,
            float duration,
            bool enableInteractionAtEnd)
        {
            if (group == null)
            {
                yield break;
            }

            group.alpha = from;
            group.interactable = false;
            group.blocksRaycasts = true;
            if (duration <= 0f)
            {
                group.alpha = to;
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                    yield return null;
                }

                group.alpha = to;
            }

            group.interactable = enableInteractionAtEnd;
            group.blocksRaycasts = enableInteractionAtEnd || to > 0f;
        }

        /// <summary>
        /// 同步玩家输入读取器的启用状态；展示层只禁止输入采样，不改写玩家状态机。
        /// </summary>
        /// <param name="enabled">是否允许玩家输入读取器继续采样 Gameplay Action Map。</param>
        private void SetCombatInputEnabled(bool enabled)
        {
            if (playerInputReader != null)
            {
                playerInputReader.enabled = enabled;
            }
        }

        /// <summary>
        /// 禁用战斗相机控制并把主相机移动到场景配置的 Eve 标题近景锚点。
        /// </summary>
        private void ApplyTitleCamera()
        {
            if (playerCameraRig != null)
            {
                playerCameraRig.enabled = false;
            }

            if (presentationCamera == null || titleCameraAnchor == null)
            {
                return;
            }

            presentationCamera.transform.SetPositionAndRotation(
                titleCameraAnchor.position,
                titleCameraAnchor.rotation);
            presentationCamera.fieldOfView = titleCameraFieldOfView;
        }

        /// <summary>
        /// 恢复进入标题前记录的相机位姿和 FOV，并按调用方要求重新启用战斗相机控制。
        /// </summary>
        /// <param name="enableRig">恢复位姿后是否启用 PlayerCameraRig。</param>
        private void RestoreGameplayCamera(bool enableRig)
        {
            if (presentationCamera != null)
            {
                presentationCamera.transform.SetPositionAndRotation(
                    gameplayCameraPosition,
                    gameplayCameraRotation);
                presentationCamera.fieldOfView = gameplayCameraFieldOfView;
            }

            if (playerCameraRig != null)
            {
                playerCameraRig.enabled = enableRig;
            }
        }

        /// <summary>
        /// 在 PlayerCameraRig 保持关闭时写入开场镜头位姿与视野角，避免相机控制器和协程争抢 Transform。
        /// </summary>
        /// <param name="position">本帧开场镜头世界坐标。</param>
        /// <param name="rotation">本帧开场镜头世界旋转。</param>
        /// <param name="fieldOfView">本帧透视相机垂直视野角，单位为度。</param>
        private void ApplyOpeningCameraPose(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            if (playerCameraRig != null)
            {
                playerCameraRig.enabled = false;
            }

            if (presentationCamera == null)
            {
                return;
            }

            presentationCamera.transform.SetPositionAndRotation(position, rotation);
            presentationCamera.fieldOfView = fieldOfView;
        }

        /// <summary>
        /// 解析当前 Boss 层级中的正式 LockOnTarget，确保开场镜头和玩家自动锁定使用同一目标点。
        /// </summary>
        /// <returns>Boss 子层级中首个可用锁定目标；Boss 或组件缺失时返回 null。</returns>
        private LockOnTarget ResolveBossLockOnTarget()
        {
            return bossActor != null
                ? bossActor.GetComponentInChildren<LockOnTarget>(true)
                : null;
        }

        /// <summary>
        /// 只在标题与开场镜头阶段启用角色补光，进入战斗后恢复由场景全局灯光负责。
        /// </summary>
        /// <param name="enabled">true 表示启用标题角色补光；false 表示关闭。</param>
        private void SetTitleCharacterLight(bool enabled)
        {
            if (titleCharacterLight != null)
            {
                titleCharacterLight.enabled = enabled;
            }
        }

        /// <summary>
        /// 控制最高层黑场遮罩的透明度与输入阻挡，供标题镜头切换使用。
        /// </summary>
        /// <param name="visible">是否让遮罩阻挡射线。</param>
        /// <param name="alpha">遮罩目标 Alpha。</param>
        private void SetTransitionVisible(bool visible, float alpha)
        {
            if (transitionGroup == null)
            {
                return;
            }

            transitionGroup.alpha = alpha;
            transitionGroup.interactable = false;
            transitionGroup.blocksRaycasts = visible;
        }

        /// <summary>
        /// 在正式战斗中响应 Escape，只负责打开或关闭暂停菜单。
        /// </summary>
        private void HandlePauseInput()
        {
            if (presentationState != DemoPresentationState.Combat)
            {
                return;
            }

            if (WasPressedEscape())
            {
                SetPaused(!paused, true);
            }
        }

        /// <summary>
        /// 在正式战斗（含暂停覆盖）中检测 H 单次按下，并切换右下角操作说明；其他展示阶段忽略该输入。
        /// </summary>
        private void HandleControlGuideInput()
        {
            if (presentationState != DemoPresentationState.Combat ||
                controlGuideRoot == null ||
                !WasPressedControlGuideToggle())
            {
                return;
            }

            SetControlGuideVisible(!controlGuideRoot.gameObject.activeSelf);
        }

        /// <summary>
        /// 只切换右下角操作说明对象，不改变战斗 HUD、暂停菜单或调试信息的显示状态。
        /// </summary>
        /// <param name="visible">为 <see langword="true"/> 时显示操作说明；为 <see langword="false"/> 时隐藏。</param>
        private void SetControlGuideVisible(bool visible)
        {
            if (controlGuideRoot != null)
            {
                controlGuideRoot.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 刷新 Hud 数据，使显示、缓存或运行时状态与当前配置保持一致。
        /// </summary>
        private void RefreshHud()
        {
            if (playerStateMachine != null)
            {
                CombatResourceSet resources = playerStateMachine.Context.Resources;
                beBar.SetValue(resources.BetaEnergy, resources.MaxBetaEnergy, false);
                hpBar.SetValue(resources.CurrentHp, resources.MaxHp, false);
                if (beValueText != null)
                {
                    beValueText.text = $"{resources.BetaEnergy:0} / {resources.MaxBetaEnergy:0}";
                }

                if (hpValueText != null)
                {
                    hpValueText.text = $"{resources.CurrentHp:0} / {resources.MaxHp:0}";
                }

            }

            if (bossResource != null)
            {
                bossHpBar.SetValue(bossResource.CurrentHp, bossResource.MaxHp, false);
            }

            if (bossActor != null)
            {
                bossShieldBar.SetValue(bossActor.CurrentShieldDefense, bossActor.MaxShieldDefense, false);
                if (bossShieldValueText != null)
                {
                    bossShieldValueText.text = $"{bossActor.CurrentShieldDefense} / {bossActor.MaxShieldDefense}";
                }
            }

            if (bossStatusText != null)
            {
                string state = bossActor != null && bossActor.IsRunning ? "ON" : "OFF";
                bossStatusText.text = $"Boss: {state}";
            }

            if (bossAiDebugText != null)
            {
                string pressure = bossActor != null ? bossActor.TempoPressure.ToString("0.0") : "--";
                string pressureMode = bossActor != null && bossActor.PressureDecayMode ? "DECAY" : "NORMAL";
                string distance = bossActor != null && playerStateMachine != null
                    ? $"{Vector3.Distance(bossActor.transform.position, playerStateMachine.transform.position):0.00} m"
                    : "--";
                string pool = bossActor != null ? bossActor.LastSelectedActionPool.ToString() : "None";
                string action = bossActor != null && !string.IsNullOrEmpty(bossActor.LastSelectedActionId)
                    ? bossActor.LastSelectedActionId
                    : "None";
                bossAiDebugText.text =
                    $"PRESSURE  {pressure}  [{pressureMode}]\n" +
                    $"DISTANCE  {distance}\n" +
                    $"POOL      {pool}\n" +
                    $"ACTION    {action}";
            }
        }

        private void RestartScene()
        {
            ReloadDemo(false);
        }

        /// <summary>
        /// 记录一次性“跳过标题”意图并重载 Demo，使再次挑战直接恢复战斗。
        /// </summary>
        private void RetryBattle()
        {
            ReloadDemo(true);
        }

        /// <summary>
        /// 清除一次性重试意图并重载 Demo，使返回流程重新显示标题页。
        /// </summary>
        private void ReturnToTitle()
        {
            ReloadDemo(false);
        }

        /// <summary>
        /// 在重载前恢复所有全局时间状态，并按目标入口写入仅跨本次场景重载有效的启动意图。
        /// </summary>
        /// <param name="skipTitle">重载后是否跳过标题并直接进入战斗。</param>
        private void ReloadDemo(bool skipTitle)
        {
            StopBackgroundMusic();
            startCombatAfterReload = skipTitle;
            hitStop?.CancelAllTimeDilation();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = restoreFixedDeltaTime;
            SceneManager.LoadScene("Demo");
        }

        /// <summary>
        /// 处理退出 Game 请求，并区分编辑器和运行时环境。
        /// </summary>
        private void QuitGame()
        {
            StopBackgroundMusic();
            hitStop?.CancelAllTimeDilation();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = restoreFixedDeltaTime;
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 设置 Paused 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        private void SetPaused(bool value, bool updateCursor)
        {
            if (value && presentationState != DemoPresentationState.Combat)
            {
                return;
            }

            if (paused == value)
            {
                SetGroupVisible(pauseGroup, value);
                return;
            }

            paused = value;
            if (paused)
            {
                hitStop?.CancelAllTimeDilation();
                restoreFixedDeltaTime = Time.fixedDeltaTime;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = restoreFixedDeltaTime;
            }

            SetGroupVisible(pauseGroup, paused);
            if (updateCursor)
            {
                Cursor.visible = paused;
                Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            }
        }

        /// <summary>
        /// 根据 Demo 启动配置切换右上角 Boss AI 信息与可选的详细调试覆盖层。
        /// </summary>
        private void SetDebugVisible(bool value)
        {
            debugVisible = value;
            SetGroupVisible(debugGroup, value);
            debugOverlay?.SetVisible(value && showDetailedDebugOverlay);
        }

        /// <summary>
        /// 构建 Ui 数据结构，供运行时、编辑器或调试显示使用。
        /// </summary>
        private void BuildUi()
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject("DemoBattleUICanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = (RectTransform)canvasObject.transform;
            StretchToParent(root);

            hudGroup = CreateGroup(root, "HUD");
            BuildBossHud((RectTransform)hudGroup.transform);
            BuildPlayerHud((RectTransform)hudGroup.transform);
            BuildControlGuide((RectTransform)hudGroup.transform);

            debugGroup = CreateGroup(root, "DebugOverlay");
            BuildBossAiDebugPanel((RectTransform)debugGroup.transform);

            pauseGroup = CreateGroup(root, "PauseMenu");
            BuildPauseMenu((RectTransform)pauseGroup.transform);

            titleGroup = CreateGroup(root, "TitleScreen");
            BuildTitleScreen((RectTransform)titleGroup.transform);

            resultGroup = CreateGroup(root, "ResultScreen");
            BuildResultScreen((RectTransform)resultGroup.transform);

            transitionGroup = CreateGroup(root, "TransitionBlackout");
            Image transitionImage = transitionGroup.GetComponent<Image>();
            transitionImage.color = Color.black;
        }

        /// <summary>
        /// 构建暗色实时角色近景标题页，左侧保留标题、安全区和两项主菜单操作。
        /// </summary>
        /// <param name="parent">标题页全屏根节点。</param>
        private void BuildTitleScreen(RectTransform parent)
        {
            Image gradient = CreateImage(parent, "NearBlackGradient", presentationGradient, Color.white);
            StretchToParent(gradient.rectTransform);
            if (presentationGradient == null)
            {
                gradient.color = new Color(0.01f, 0.018f, 0.025f, 0.84f);
            }

            RectTransform leftContent = CreatePanel(parent, "LeftContent", new Color(0f, 0f, 0f, 0f));
            Anchor(
                leftContent,
                new Vector2(0f, 0f),
                new Vector2(0.55f, 1f),
                new Vector2(0f, 0.5f),
                Vector2.zero,
                Vector2.zero);

            Image topRule = CreateImage(leftContent, "TopCyanRule", null, PresentationCyan);
            Anchor(
                topRule.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(168f, -170f),
                new Vector2(92f, 3f));

            Text category = CreateText(
                leftContent,
                "Category",
                "PROJECT EVE // COMBAT PROTOCOL",
                20,
                new Color(0.55f, 0.7f, 0.75f, 0.86f),
                TextAnchor.MiddleLeft);
            Anchor(
                category.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(168f, -198f),
                new Vector2(720f, 32f));

            Text title = CreateText(
                leftContent,
                "MainTitle",
                "伊芙计划",
                92,
                PresentationWhite,
                TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            Anchor(
                title.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(160f, -226f),
                new Vector2(900f, 180f));

            Text subtitle = CreateText(
                leftContent,
                "Subtitle",
                "渡鸦战斗演示",
                30,
                new Color(0.72f, 0.78f, 0.81f, 0.94f),
                TextAnchor.MiddleLeft);
            Anchor(
                subtitle.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(168f, -394f),
                new Vector2(720f, 48f));

            titleStartButton = CreatePresentationButton(
                leftContent,
                "StartBattleButton",
                "开始战斗",
                new Vector2(168f, -610f),
                StartCombatFromTitle);
            CreatePresentationButton(
                leftContent,
                "QuitGameButton",
                "退出游戏",
                new Vector2(168f, -700f),
                QuitGame);

            Text footer = CreateText(
                leftContent,
                "Footer",
                "RAVEN COMBAT DEMONSTRATION  /  2026",
                17,
                new Color(0.48f, 0.56f, 0.6f, 0.72f),
                TextAnchor.MiddleLeft);
            Anchor(
                footer.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(168f, 82f),
                new Vector2(720f, 30f));
        }

        /// <summary>
        /// 构建胜利和失败共用的结算页，结果文案由首次死亡判定在显示前写入。
        /// </summary>
        /// <param name="parent">结算页全屏根节点。</param>
        private void BuildResultScreen(RectTransform parent)
        {
            Image gradient = CreateImage(parent, "ResultGradient", presentationGradient, Color.white);
            StretchToParent(gradient.rectTransform);
            if (presentationGradient == null)
            {
                gradient.color = new Color(0.005f, 0.01f, 0.015f, 0.94f);
            }

            Image scanlines = CreateImage(
                parent,
                "ResultScanlines",
                presentationScanlines,
                new Color(0.35f, 0.8f, 0.9f, 0.1f));
            StretchToParent(scanlines.rectTransform);
            if (presentationScanlines != null)
            {
                scanlines.type = Image.Type.Tiled;
            }

            RectTransform panel = CreatePanel(parent, "ResultContent", new Color(0f, 0f, 0f, 0f));
            Anchor(
                panel,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 20f),
                new Vector2(1260f, 740f));

            Image rule = CreateImage(panel, "ResultCyanRule", null, PresentationCyan);
            Anchor(
                rule.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -58f),
                new Vector2(140f, 4f));

            resultTitleText = CreateText(
                panel,
                "ResultTitle",
                "作战完成",
                88,
                PresentationWhite,
                TextAnchor.MiddleCenter);
            resultTitleText.fontStyle = FontStyle.Bold;
            Anchor(
                resultTitleText.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -78f),
                new Vector2(0f, 180f));

            resultSubtitleText = CreateText(
                panel,
                "ResultSubtitle",
                "目标已清除：渡鸦",
                30,
                new Color(0.67f, 0.75f, 0.79f, 0.95f),
                TextAnchor.MiddleCenter);
            Anchor(
                resultSubtitleText.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -270f),
                new Vector2(0f, 46f));

            Text resultTimeLabel = CreateText(
                panel,
                "BattleTimeLabel",
                "战斗时间",
                30,
                new Color(0.72f, 0.8f, 0.83f, 0.96f),
                TextAnchor.MiddleRight);
            Anchor(
                resultTimeLabel.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-24f, -342f),
                new Vector2(230f, 52f));

            resultTimeText = CreateText(
                panel,
                "BattleTime",
                "00:00.00",
                34,
                PresentationCyan,
                TextAnchor.MiddleLeft);
            if (battleTimerFont != null)
            {
                resultTimeText.font = battleTimerFont;
            }

            Anchor(
                resultTimeText.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -342f),
                new Vector2(300f, 52f));

            resultRetryButton = CreatePresentationButton(
                panel,
                "RetryButton",
                "再次挑战",
                new Vector2(370f, -480f),
                RetryBattle);
            CreatePresentationButton(
                panel,
                "ReturnTitleButton",
                "返回标题",
                new Vector2(370f, -570f),
                ReturnToTitle);
            CreatePresentationButton(
                panel,
                "ResultQuitButton",
                "退出游戏",
                new Vector2(370f, -660f),
                QuitGame);
        }

        /// <summary>
        /// 在屏幕顶部中央创建 Boss 名称、HP 条和按当前最大护盾值生成的分段护盾条。
        /// </summary>
        /// <param name="parent">承载 Boss HUD 的全屏 HUD 组 RectTransform。</param>
        private void BuildBossHud(RectTransform parent)
        {
            RectTransform root = CreatePanel(parent, "BossHUD", new Color(0f, 0f, 0f, 0f));
            Anchor(root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(980f, 116f));

            Text nameText = CreateText(root, "BossName", bossDisplayName, 23, White, TextAnchor.MiddleCenter);
            Anchor(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(0f, 28f));

            bossStatusText = CreateText(root, "BossStatus", "Boss: OFF", 16, new Color(0.75f, 0.86f, 0.86f, 0.86f), TextAnchor.MiddleRight);
            Anchor(bossStatusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -10f), new Vector2(140f, 24f));

            bossHpValueText = CreateText(root, "BossHpValue", string.Empty, 18, White, TextAnchor.MiddleRight);
            bossHpBar = CreateSegmentedBar(root, "BossHpBar", 96, 12, new Vector2(7f, 7f), White, Empty, bossHpValueText);
            RectTransform bossHpBarRect = (RectTransform)bossHpBar.transform;
            Anchor(bossHpBarRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(bossHpBarRect.sizeDelta.x, 18f));
            Anchor(bossHpValueText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(380f, -42f), new Vector2(180f, 24f));

            bossShieldValueText = CreateText(root, "BossShieldValue", string.Empty, 16, Cyan, TextAnchor.MiddleRight);
            int shieldSegmentCount = bossActor != null ? Mathf.Max(1, bossActor.MaxShieldDefense) : 1;
            bossShieldBar = CreateSegmentedBar(root, "BossShieldBar", shieldSegmentCount, 5, new Vector2(16f, 6f), Cyan, Empty, bossShieldValueText);
            RectTransform bossShieldBarRect = (RectTransform)bossShieldBar.transform;
            Anchor(bossShieldBarRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(bossShieldBarRect.sizeDelta.x, 14f));
            Anchor(bossShieldValueText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(210f, -60f), new Vector2(150f, 22f));
        }

        /// <summary>
        /// 构建 Player / Hud 数据结构，供运行时、编辑器或调试显示使用。
        /// </summary>
        private void BuildPlayerHud(RectTransform parent)
        {
            RectTransform root = CreatePanel(parent, "PlayerHUD", new Color(0f, 0f, 0f, 0f));
            Anchor(root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(112f, 86f), new Vector2(1120f, 112f));

            beBar = BuildResourceRow(root, "BE", 0f, 56f, 32, GuardBlue, out beValueText);
            hpBar = BuildResourceRow(root, "HP", 0f, 18f, 72, White, out hpValueText);
        }

        /// <summary>
        /// 构建 Resource / Row 数据结构，供运行时、编辑器或调试显示使用。
        /// </summary>
        private SegmentedBarView BuildResourceRow(RectTransform root, string label, float x, float y, int count, Color color, out Text valueText)
        {
            Text labelText = CreateText(root, $"{label}_Label", label, 24, new Color(0.82f, 0.86f, 0.88f, 0.9f), TextAnchor.MiddleRight);
            Anchor(labelText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(76f, 28f));

            valueText = CreateText(root, $"{label}_Value", string.Empty, 20, White, TextAnchor.MiddleLeft);
            Anchor(valueText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(902f, y), new Vector2(180f, 28f));

            SegmentedBarView bar = CreateSegmentedBar(root, $"{label}_Bar", count, 8, new Vector2(8f, 8f), color, Empty, valueText);
            Anchor((RectTransform)bar.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(96f, y), new Vector2(790f, 18f));
            return bar;
        }

        /// <summary>
        /// 在战斗 HUD 右下角构建常驻的两列静态操作说明，不读取输入状态或技能可用性。
        /// </summary>
        /// <param name="parent">承载操作说明的全屏战斗 HUD。</param>
        private void BuildControlGuide(RectTransform parent)
        {
            RectTransform root = CreatePanel(parent, "ControlGuide", new Color(0.015f, 0.025f, 0.03f, 0.72f));
            controlGuideRoot = root;
            Anchor(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-72f, 72f), new Vector2(620f, 230f));
            root.GetComponent<Image>().raycastTarget = false;

            Text title = CreateText(root, "Title", "H 键隐藏", 26, PresentationWhite, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -7f), new Vector2(580f, 40f));

            Image accentLine = CreateImage(root, "AccentLine", null, Cyan);
            Anchor(accentLine.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -50f), new Vector2(580f, 2f));
            accentLine.raycastTarget = false;

            Text mouseLabel = CreateText(root, "MouseColumnLabel", "鼠标", 18, Cyan, TextAnchor.MiddleLeft);
            Anchor(mouseLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -58f), new Vector2(280f, 28f));

            Text keyboardLabel = CreateText(root, "KeyboardColumnLabel", "键盘", 18, Cyan, TextAnchor.MiddleLeft);
            Anchor(keyboardLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(320f, -58f), new Vector2(280f, 28f));

            BuildControlGuideRow(root, "MouseLightAttack", "鼠标左键", "轻攻击", new Vector2(20f, -92f));
            BuildControlGuideRow(root, "MouseHeavyAttack", "鼠标右键", "重攻击", new Vector2(20f, -134f));
            BuildControlGuideRow(root, "MouseLockOn", "鼠标中键", "锁定", new Vector2(20f, -176f));
            BuildControlGuideRow(root, "KeyboardEvade", "Shift", "闪避", new Vector2(320f, -92f));
            BuildControlGuideRow(root, "KeyboardGuard", "E", "防御", new Vector2(320f, -134f));
            BuildControlGuideRow(root, "KeyboardSkill1", "1", "释放技能", new Vector2(320f, -176f));
        }

        /// <summary>
        /// 创建一行不可交互的按键说明，并把按键与对应战斗动作并排展示。
        /// </summary>
        /// <param name="root">承载本行的战斗操作说明面板。</param>
        /// <param name="name">用于测试和层级定位的行对象名称。</param>
        /// <param name="key">展示在统一宽度键帽中的主要按键名称。</param>
        /// <param name="action">展示在键帽右侧的战斗动作名称。</param>
        /// <param name="position">本行相对说明面板左上角的位置。</param>
        private void BuildControlGuideRow(RectTransform root, string name, string key, string action, Vector2 position)
        {
            RectTransform row = CreatePanel(root, name, Color.clear);
            Anchor(row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(280f, 34f));
            row.GetComponent<Image>().raycastTarget = false;

            RectTransform keyCap = CreatePanel(row, "Key", new Color(0.06f, 0.08f, 0.1f, 0.86f));
            Anchor(keyCap, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(116f, 32f));
            keyCap.GetComponent<Image>().raycastTarget = false;

            Text keyText = CreateText(keyCap, "Text", key, 18, White, TextAnchor.MiddleCenter);
            StretchToParent(keyText.rectTransform);

            Text actionText = CreateText(row, "Action", action, 20, White, TextAnchor.MiddleLeft);
            Anchor(actionText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(132f, 0f), new Vector2(148f, 34f));
        }

        /// <summary>
        /// 在开发调试组右上角创建 Boss AI 只读面板，显示双方直线距离、正式压力与最近一次选招结果。
        /// </summary>
        /// <param name="parent">承载右上角 Boss AI 调试 UI 的全屏 RectTransform。</param>
        private void BuildBossAiDebugPanel(RectTransform parent)
        {
            RectTransform panel = CreatePanel(parent, "BossAiDebugPanel", DimPanel);
            Anchor(
                panel,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-42f, -42f),
                new Vector2(460f, 154f));

            bossAiDebugText = CreateText(
                panel,
                "BossAiDebugText",
                "PRESSURE  --  [NORMAL]\nDISTANCE  --\nPOOL      None\nACTION    None",
                20,
                GuardBlue,
                TextAnchor.UpperLeft);
            Anchor(
                bossAiDebugText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-32f, -24f));
        }

        private void BuildPauseMenu(RectTransform parent)
        {
            Image dim = CreateImage(parent, "Dim", null, new Color(0f, 0f, 0f, 0.58f));
            StretchToParent(dim.rectTransform);

            RectTransform panel = CreatePanel(parent, "PausePanel", DimPanel);
            Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 310f));

            Text title = CreateText(panel, "Title", "PAUSE", 36, White, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(0f, 50f));

            CreateMenuButton(panel, "ResumeButton", "继续", new Vector2(0f, -106f), () => SetPaused(false, true));
            CreateMenuButton(panel, "RestartButton", "重新开始", new Vector2(0f, -166f), RestartScene);
            CreateMenuButton(panel, "QuitButton", "退出游戏", new Vector2(0f, -226f), QuitGame);
        }

        private Text CreateMenuButton(RectTransform parent, string name, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction action)
        {
            RectTransform buttonRect = CreatePanel(parent, name, new Color(0.06f, 0.08f, 0.1f, 0.86f));
            Anchor(buttonRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), anchoredPosition, new Vector2(280f, 44f));
            Button button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonRect.GetComponent<Image>();
            button.onClick.AddListener(action);
            Text text = CreateText(buttonRect, "Text", label, 22, White, TextAnchor.MiddleCenter);
            StretchToParent(text.rectTransform);
            return text;
        }

        /// <summary>
        /// 创建标题与结算页共用的左对齐菜单按钮，并配置冷青高亮和键盘导航颜色。
        /// </summary>
        /// <param name="parent">承载按钮的标题或结算内容根节点。</param>
        /// <param name="name">用于层级和测试定位的 GameObject 名称。</param>
        /// <param name="label">按钮显示的中文操作文本。</param>
        /// <param name="anchoredPosition">相对父节点左上锚点的 UI 坐标。</param>
        /// <param name="action">点击后执行的展示流程操作。</param>
        /// <returns>创建并完成颜色导航配置的 Button。</returns>
        private Button CreatePresentationButton(
            RectTransform parent,
            string name,
            string label,
            Vector2 anchoredPosition,
            UnityEngine.Events.UnityAction action)
        {
            RectTransform buttonRect = CreatePanel(parent, name, new Color(0.025f, 0.05f, 0.065f, 0.68f));
            Anchor(
                buttonRect,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                anchoredPosition,
                new Vector2(520f, 72f));

            Image sideLine = CreateImage(buttonRect, "SelectionLine", null, PresentationCyan);
            Anchor(
                sideLine.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0.5f),
                Vector2.zero,
                new Vector2(5f, -14f));

            Button button = buttonRect.gameObject.AddComponent<Button>();
            Image buttonBackground = buttonRect.GetComponent<Image>();
            buttonBackground.color = Color.white;
            button.targetGraphic = buttonBackground;
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.018f, 0.035f, 0.045f, 0.92f);
            colors.highlightedColor = new Color(0.08f, 0.24f, 0.29f, 0.9f);
            colors.selectedColor = new Color(0.065f, 0.2f, 0.25f, 0.88f);
            colors.pressedColor = new Color(0.12f, 0.42f, 0.5f, 0.96f);
            colors.disabledColor = new Color(0.2f, 0.25f, 0.28f, 0.38f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Text text = CreateText(buttonRect, "Text", label, 30, PresentationWhite, TextAnchor.MiddleLeft);
            text.fontStyle = FontStyle.Bold;
            Anchor(
                text.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(34f, 0f),
                new Vector2(-68f, 0f));
            return button;
        }

        private SegmentedBarView CreateSegmentedBar(RectTransform parent, string name, int count, int groupSize, Vector2 size, Color filled, Color empty, Text valueText)
        {
            GameObject barObject = new GameObject(name, typeof(RectTransform));
            barObject.transform.SetParent(parent, false);
            SegmentedBarView bar = barObject.AddComponent<SegmentedBarView>();
            bar.Configure(count, groupSize, size, filled, empty, valueText);
            return bar;
        }

        private CanvasGroup CreateGroup(RectTransform parent, string name)
        {
            RectTransform rectTransform = CreatePanel(parent, name, new Color(0f, 0f, 0f, 0f));
            StretchToParent(rectTransform);
            return rectTransform.gameObject.AddComponent<CanvasGroup>();
        }

        private RectTransform CreatePanel(RectTransform parent, string name, Color color)
        {
            GameObject panelObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(parent, false);
            Image image = panelObject.GetComponent<Image>();
            image.color = color;
            return (RectTransform)panelObject.transform;
        }

        private Image CreateImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return image;
        }

        /// <summary>
        /// 根据当前展示状态和正式 Lock-on 上下文切换 Raven 骨骼子节点上的世界空间白点。
        /// </summary>
        private void UpdateLockOnIndicator()
        {
            if (lockOnIndicatorWorldMarker == null)
            {
                return;
            }

            Transform target = playerStateMachine != null
                ? playerStateMachine.Context.LockOnTarget
                : null;
            bool targetsCurrentBoss = target != null &&
                bossActor != null &&
                (target == bossActor.transform || target.IsChildOf(bossActor.transform));
            bool visible = presentationState == DemoPresentationState.Combat &&
                !paused &&
                playerStateMachine != null &&
                playerStateMachine.Context.IsLockOn &&
                targetsCurrentBoss &&
                target.gameObject.activeInHierarchy;
            SetLockOnIndicatorVisible(visible);
        }

        /// <summary>
        /// 仅在显隐状态变化时重播或清空骨骼子节点粒子，确保每次重新锁定都从柔光首帧开始。
        /// </summary>
        /// <param name="visible">true 表示当前正式目标处于锁定显示阶段。</param>
        private void SetLockOnIndicatorVisible(bool visible)
        {
            GameObject markerObject = lockOnIndicatorWorldMarker != null
                ? lockOnIndicatorWorldMarker.gameObject
                : null;
            if (markerObject == null || markerObject.activeSelf == visible)
            {
                return;
            }

            if (visible)
            {
                markerObject.SetActive(true);
                lockOnIndicatorWorldMarker.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                lockOnIndicatorWorldMarker.Play(true);
            }
            else
            {
                lockOnIndicatorWorldMarker.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                markerObject.SetActive(false);
            }
        }

        private Text CreateText(RectTransform parent, string name, string text, int fontSize, Color color, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text uiText = textObject.GetComponent<Text>();
            uiText.font = uiFont;
            uiText.text = text;
            uiText.fontSize = fontSize;
            uiText.color = color;
            uiText.alignment = alignment;
            uiText.raycastTarget = false;
            return uiText;
        }

        private void BindReferences()
        {
            if (playerStateMachine == null)
            {
                playerStateMachine = FindFirstObjectByType<PlayerStateMachine>();
            }

            if (playerInputReader == null && playerStateMachine != null)
            {
                playerInputReader = playerStateMachine.GetComponent<PlayerInputReader>();
            }

            if (playerCameraRig == null)
            {
                playerCameraRig = FindFirstObjectByType<PlayerCameraRig>();
            }

            if (titleCameraAnchor == null && playerStateMachine != null)
            {
                titleCameraAnchor = playerStateMachine.transform.Find("TitleCameraAnchor");
            }

            if (titleCameraAnchor == null)
            {
                GameObject titleAnchorObject = GameObject.Find("TitleCameraAnchor");
                titleCameraAnchor = titleAnchorObject != null
                    ? titleAnchorObject.transform
                    : null;
            }

            if (titleCharacterLight == null && titleCameraAnchor != null)
            {
                Transform lightTransform = titleCameraAnchor.Find("TitleCharacterKeyLight");
                titleCharacterLight = lightTransform != null
                    ? lightTransform.GetComponent<Light>()
                    : null;
            }

            if (bossActor == null)
            {
                bossActor = FindFirstObjectByType<BossActor>();
            }

            if (bossResource == null && bossActor != null)
            {
                bossResource = bossActor.GetComponent<CombatResourceComponent>();
            }

            if (hitStop == null)
            {
                hitStop = FindFirstObjectByType<CombatHitStop>();
            }

            if (debugOverlay == null)
            {
                debugOverlay = FindFirstObjectByType<EveDebugOverlay>();
            }

            if (legacyHudOverlay == null)
            {
                legacyHudOverlay = FindFirstObjectByType<CombatHudOverlay>();
            }
        }

  
        private static void SetGroupVisible(CanvasGroup group, bool visible)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }


        private static void Anchor(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.pivot = pivot;
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = sizeDelta;
        }


        private static void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
        }


        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
#else
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            DontDestroyOnLoad(eventSystem);
        }


        private static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static bool WasPressedEscape()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        /// <summary>
        /// 读取本帧 H 键的单次按下状态，专供战斗操作说明显隐切换使用。
        /// </summary>
        /// <returns>本帧首次按下 H 键时返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        private static bool WasPressedControlGuideToggle()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.hKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.H);
#endif
        }

    }
}
