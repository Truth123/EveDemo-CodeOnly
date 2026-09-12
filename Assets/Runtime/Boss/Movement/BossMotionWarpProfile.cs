// 文件说明：维护 Boss 代码位移、Root Motion、MotionWarp、碰撞裁剪和调试快照。
// 所属模块：Boss 位移。
// 运行影响：影响 Boss 空间位置、攻击落点、反应位移、穿模保护和位移调试。

using ProjectEVE.Boss.AI;
using System;
using UnityEngine;

namespace ProjectEVE.Boss.Movement
{
    /// <summary>
    /// Boss 位移 Warp 模式。Scale 只缩放位移长度，Skew 可额外偏移方向，CodeDriven 用代码生成位移。
    /// </summary>
    public enum BossWarpMode
    {
        ScaleTranslation = 0,
        SkewTranslationRotation = 1,
        CodeDriven = 2
    }

    /// <summary>
    /// Warp 目标解析方式。ToAttackPoint 的 StopDistance 只表示从远处接近玩家时的停靠距离，过近时不会生成 Boss 身后的反向落点。
    /// </summary>
    public enum BossWarpTargetingMode
    {
        ToAttackPoint = 0,
        AwayFromTarget = 1,
        FixedForward = 2,
        Strafe = 3,
        OrbitAroundTarget = 4
    }

    public enum BossOrbitSide
    {
        Left = 0,
        Right = 1,
        Random = 2
    }

    /// <summary>
    /// CodeMove 窗口的位移进度模式。方向仍由 TargetingMode 决定。
    /// </summary>
    public enum BossCodeMoveProgressMode
    {
        ConstantSpeed = 0,
        CurveDistance = 1
    }

    /// <summary>
    /// Raven Boss 位移配置资产。用于把 Motion Warp / CodeMove 调参从伤害和反应语义中拆离。
    /// </summary>
    [CreateAssetMenu(fileName = "RavenBossMotionWarpProfile", menuName = "Project EVE/Boss/Motion Warp Profile")]
    public sealed class BossMotionWarpProfile : ScriptableObject
    {
        /// <summary>每个 ActionId 对应一份位移配置，普通攻击和 Boss Reaction CodeMove 共用该表。</summary>
        [SerializeField] private BossAttackMotionConfig[] attacks = Array.Empty<BossAttackMotionConfig>();

        public BossAttackMotionConfig[] Attacks => attacks;

        /// <summary>供 Editor 迁移工具一次性写入完整动作位移配置。</summary>
        public void Configure(BossAttackMotionConfig[] nextAttacks)
        {
            attacks = nextAttacks ?? Array.Empty<BossAttackMotionConfig>();
            OnValidate();
        }

        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            if (attacks == null)
            {
                attacks = Array.Empty<BossAttackMotionConfig>();
                return;
            }

            for (int i = 0; i < attacks.Length; i++)
            {
                attacks[i]?.Validate();
            }
        }

        /// <summary>按 ActionId 查询显式配置；缺失配置由调用方拒绝动作位移。</summary>
        public bool TryGetConfig(string attackId, out BossAttackMotionConfig config)
        {
            if (!string.IsNullOrEmpty(attackId) && attacks != null)
            {
                for (int i = 0; i < attacks.Length; i++)
                {
                    if (attacks[i] != null && attacks[i].AttackId == attackId)
                    {
                        config = attacks[i];
                        return true;
                    }
                }
            }

            config = null;
            return false;
        }
    }

    /// <summary>单个 Boss action 的位移和出招空间配置。</summary>
    [Serializable]
    public sealed class BossAttackMotionConfig
    {
        public string AttackId;
        public BossAttackMotionMode MotionMode;
        public BossAttackSelectionProfile Selection;
        public BossMotionWarpWindow[] WarpWindows;
        public BossRootMotionSuppressWindow[] RootMotionSuppressWindows;
        public BossCodeMoveWindow[] CodeMoveWindows;

        /// <summary>
        /// 执行 Boss / Attack / Motion / Config 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public BossAttackMotionConfig(
            string attackId,
            BossAttackMotionMode motionMode,
            BossAttackSelectionProfile selection,
            BossMotionWarpWindow[] warpWindows,
            BossRootMotionSuppressWindow[] rootMotionSuppressWindows,
            BossCodeMoveWindow[] codeMoveWindows)
        {
            AttackId = attackId;
            MotionMode = motionMode;
            Selection = selection ?? new BossAttackSelectionProfile();
            WarpWindows = warpWindows ?? Array.Empty<BossMotionWarpWindow>();
            RootMotionSuppressWindows = rootMotionSuppressWindows ?? Array.Empty<BossRootMotionSuppressWindow>();
            CodeMoveWindows = codeMoveWindows ?? Array.Empty<BossCodeMoveWindow>();
        }

        /// <summary>
        /// 校验 Validate 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        public void Validate()
        {
            Selection ??= new BossAttackSelectionProfile();
            WarpWindows ??= Array.Empty<BossMotionWarpWindow>();
            RootMotionSuppressWindows ??= Array.Empty<BossRootMotionSuppressWindow>();
            CodeMoveWindows ??= Array.Empty<BossCodeMoveWindow>();

            for (int i = 0; i < CodeMoveWindows.Length; i++)
            {
                CodeMoveWindows[i]?.Validate();
            }
        }
    }

    /// <summary>AI 出招前的基础可行性检查，不替代攻击执行中的 Motion Warp。</summary>
    [Serializable]
    public sealed class BossAttackSelectionProfile
    {
        public float MinStartDistance = 0f;
        public float MaxStartDistance = 6f;
        public float RequiredForwardClearance = 0f;
        public float RequiredBackwardClearance = 0f;

        /// <summary>
        /// 执行 Boss / Attack / Selection / Profile 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public BossAttackSelectionProfile()
        {
        }

        /// <summary>
        /// 执行 Boss / Attack / Selection / Profile 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public BossAttackSelectionProfile(float minStartDistance, float maxStartDistance, float requiredForwardClearance, float requiredBackwardClearance)
        {
            MinStartDistance = Mathf.Max(0f, minStartDistance);
            MaxStartDistance = Mathf.Max(MinStartDistance, maxStartDistance);
            RequiredForwardClearance = Mathf.Max(0f, requiredForwardClearance);
            RequiredBackwardClearance = Mathf.Max(0f, requiredBackwardClearance);
        }
    }

    /// <summary>
    /// Root Motion 抑制窗口。用于处理 Warp 到落点后动画仍有残余位移的区间。
    /// TranslationScale 为 0 时会完全忽略该区间 Animator.deltaPosition。
    /// </summary>
    [Serializable]
    public sealed class BossRootMotionSuppressWindow
    {
        public string Name;
        public float StartTime;
        public float EndTime;
        public float TranslationScale = 1f;

        /// <summary>
        /// 执行 Boss / Root / Motion / Suppress / Window 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public BossRootMotionSuppressWindow()
        {
        }

        /// <summary>
        /// 执行 Boss / Root / Motion / Suppress / Window 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public BossRootMotionSuppressWindow(string name, float startTime, float endTime, float translationScale)
        {
            Name = name;
            StartTime = Mathf.Max(0f, startTime);
            EndTime = Mathf.Max(StartTime, endTime);
            TranslationScale = Mathf.Max(0f, translationScale);
        }

        /// <summary>
        /// 执行 Contains 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public bool Contains(float time)
        {
            return time >= StartTime && time <= EndTime;
        }
    }

    /// <summary>Root Motion Warp 窗口，等价于 Unreal Motion Warping 的可调 Warp Window。</summary>
    [Serializable]
    public sealed class BossMotionWarpWindow
    {
        public string Name;
        public float StartTime;
        public float EndTime;
        public BossWarpMode WarpMode;
        public BossWarpTargetingMode TargetingMode;
        /// <summary>从远处接近玩家时希望保留的停靠距离；距离不足时位移求解会退化为原地而不是反向穿越。</summary>
        public float StopDistance;
        public float DeadZone;
        public float MaxMovePerFrame = 0.45f;
        public float MaxYawSpeed;
        public bool FollowTarget;
        public bool WarpTranslation = true;
        public bool UseAuthoredTotalTranslation = true;
        public Vector3 AuthoredTotalTranslation = Vector3.forward;
        public bool AllowScaleUp = true;
        public bool AllowScaleDown = true;
        public float MinScaleMultiplier = 0.35f;
        public float MaxScaleMultiplier = 2.2f;

        /// <summary>
        /// 执行 Contains 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public bool Contains(float time)
        {
            return time >= StartTime && time <= EndTime;
        }

        /// <summary>
        /// 执行 Skew 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public static BossMotionWarpWindow Skew(
            string name,
            float startTime,
            float endTime,
            float stopDistance,
            float deadZone,
            float maxYawSpeed,
            bool followTarget,
            Vector3 authoredTotalTranslation)
        {
            return new BossMotionWarpWindow
            {
                Name = name,
                StartTime = startTime,
                EndTime = endTime,
                WarpMode = BossWarpMode.SkewTranslationRotation,
                TargetingMode = BossWarpTargetingMode.ToAttackPoint,
                StopDistance = stopDistance,
                DeadZone = deadZone,
                MaxMovePerFrame = 0.45f,
                MaxYawSpeed = maxYawSpeed,
                FollowTarget = followTarget,
                WarpTranslation = true,
                UseAuthoredTotalTranslation = true,
                AuthoredTotalTranslation = authoredTotalTranslation,
                AllowScaleUp = true,
                AllowScaleDown = true,
                MinScaleMultiplier = 0.35f,
                MaxScaleMultiplier = 2.2f
            };
        }
    }

    /// <summary>无 Root Motion 动画段的代码驱动 Warp 窗口。</summary>
    [Serializable]
    public sealed class BossCodeMoveWindow
    {
        private static readonly AnimationCurve DefaultDistanceCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.22f, 0f),
            new Keyframe(0.66f, 0.58f),
            new Keyframe(1f, 1f));

        public string Name;
        public float StartTime;
        public float EndTime;
        public BossWarpTargetingMode TargetingMode;
        public BossCodeMoveProgressMode ProgressMode = BossCodeMoveProgressMode.ConstantSpeed;
        public AnimationCurve NormalizedDistanceCurve;
        /// <summary>从远处接近玩家时希望保留的停靠距离；Orbit 模式不读取该字段。</summary>
        public float StopDistance;
        public float DeadZone;
        public float MaxMoveDistance;
        public float MaxMoveSpeed;
        public float MaxYawSpeed;
        public bool FollowTarget;
        public bool AllowBackwardCorrection;
        public bool UseDistanceBand;
        public BossOrbitSide OrbitSide = BossOrbitSide.Random;
        /// <summary>Orbit 模式下围绕目标水平旋转的角度；右侧为正向，左侧为反向。</summary>
        public float OrbitAngleDegrees = 90f;

        /// <summary>
        /// 执行 Contains 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public bool Contains(float time)
        {
            return time >= StartTime && time <= EndTime;
        }

        /// <summary>
        /// 按窗口归一化时间采样曲线位移进度。
        /// </summary>
        public float EvaluateDistance01(float normalizedTime)
        {
            AnimationCurve curve = NormalizedDistanceCurve;
            if (curve == null || curve.length == 0)
            {
                curve = DefaultDistanceCurve;
            }

            return Mathf.Clamp01(curve.Evaluate(Mathf.Clamp01(normalizedTime)));
        }

        /// <summary>
        /// 校验 Validate 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        public void Validate()
        {
            StartTime = Mathf.Max(0f, StartTime);
            EndTime = Mathf.Max(StartTime, EndTime);
            MaxMoveDistance = Mathf.Max(0f, MaxMoveDistance);
            MaxMoveSpeed = Mathf.Max(0f, MaxMoveSpeed);
            MaxYawSpeed = Mathf.Max(0f, MaxYawSpeed);
            StopDistance = Mathf.Max(0f, StopDistance);
            DeadZone = Mathf.Max(0f, DeadZone);
            OrbitAngleDegrees = Mathf.Clamp(OrbitAngleDegrees, 0f, 360f);

            if (ProgressMode == BossCodeMoveProgressMode.CurveDistance &&
                (NormalizedDistanceCurve == null || NormalizedDistanceCurve.length == 0))
            {
                NormalizedDistanceCurve = new AnimationCurve(DefaultDistanceCurve.keys);
            }
        }

        /// <summary>
        /// 执行 Fixed / Back 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public static BossCodeMoveWindow FixedBack(string name, float startTime, float endTime, float speed, float maxMoveDistance)
        {
            return new BossCodeMoveWindow
            {
                Name = name,
                StartTime = startTime,
                EndTime = endTime,
                TargetingMode = BossWarpTargetingMode.AwayFromTarget,
                ProgressMode = BossCodeMoveProgressMode.ConstantSpeed,
                StopDistance = 0f,
                DeadZone = 0f,
                MaxMoveDistance = maxMoveDistance,
                MaxMoveSpeed = speed,
                MaxYawSpeed = 0f,
                FollowTarget = false,
                AllowBackwardCorrection = true,
                UseDistanceBand = false
            };
        }

        /// <summary>
        /// 创建零位移的目标朝向窗口，使攻击在指定时间段内以受限角速度跟踪玩家。
        /// </summary>
        /// <param name="name">窗口名称，用于运行时状态和非跟随目标的位置锁定。</param>
        /// <param name="startTime">窗口在动作时间轴上的开始时间，单位为秒。</param>
        /// <param name="endTime">窗口在动作时间轴上的结束时间，单位为秒。</param>
        /// <param name="maxYawSpeed">每秒最大水平旋转角速度，单位为度。</param>
        /// <param name="followTarget">为 true 时每帧读取玩家当前位置；为 false 时锁定窗口首次看到的位置。</param>
        /// <returns>不会产生位移、只负责更新 Boss 水平朝向的 CodeMove 窗口。</returns>
        public static BossCodeMoveWindow FaceTarget(
            string name,
            float startTime,
            float endTime,
            float maxYawSpeed,
            bool followTarget = true)
        {
            return new BossCodeMoveWindow
            {
                Name = name,
                StartTime = Mathf.Max(0f, startTime),
                EndTime = Mathf.Max(startTime, endTime),
                TargetingMode = BossWarpTargetingMode.ToAttackPoint,
                ProgressMode = BossCodeMoveProgressMode.ConstantSpeed,
                StopDistance = 0f,
                DeadZone = 0f,
                MaxMoveDistance = 0f,
                MaxMoveSpeed = 0f,
                MaxYawSpeed = Mathf.Max(0f, maxYawSpeed),
                FollowTarget = followTarget,
                AllowBackwardCorrection = false,
                UseDistanceBand = false
            };
        }

        /// <summary>
        /// 创建远离目标的曲线位移窗口，用于受击、击倒等冲击式位移。
        /// </summary>
        public static BossCodeMoveWindow CurveBack(
            string name,
            float startTime,
            float endTime,
            float maxMoveDistance,
            float maxYawSpeed = 0f)
        {
            return new BossCodeMoveWindow
            {
                Name = name,
                StartTime = Mathf.Max(0f, startTime),
                EndTime = Mathf.Max(startTime, endTime),
                TargetingMode = BossWarpTargetingMode.AwayFromTarget,
                ProgressMode = BossCodeMoveProgressMode.CurveDistance,
                NormalizedDistanceCurve = new AnimationCurve(DefaultDistanceCurve.keys),
                StopDistance = 0f,
                DeadZone = 0f,
                MaxMoveDistance = Mathf.Max(0f, maxMoveDistance),
                MaxMoveSpeed = 0f,
                MaxYawSpeed = Mathf.Max(0f, maxYawSpeed),
                FollowTarget = false,
                AllowBackwardCorrection = true,
                UseDistanceBand = false
            };
        }

        /// <summary>
        /// 将当前对象转换为 Attack / Point 数据，供其他模块读取。
        /// </summary>
        public static BossCodeMoveWindow ToAttackPoint(
            string name,
            float startTime,
            float endTime,
            float stopDistance,
            float deadZone,
            float maxMoveDistance,
            float maxMoveSpeed,
            float maxYawSpeed,
            bool followTarget)
        {
            return new BossCodeMoveWindow
            {
                Name = name,
                StartTime = startTime,
                EndTime = endTime,
                TargetingMode = BossWarpTargetingMode.ToAttackPoint,
                ProgressMode = BossCodeMoveProgressMode.ConstantSpeed,
                StopDistance = stopDistance,
                DeadZone = deadZone,
                MaxMoveDistance = maxMoveDistance,
                MaxMoveSpeed = maxMoveSpeed,
                MaxYawSpeed = maxYawSpeed,
                FollowTarget = followTarget,
                AllowBackwardCorrection = false,
                UseDistanceBand = true
            };
        }

        /// <summary>
        /// 创建按角度围绕目标旋转的 Orbit 窗口；实际半径由窗口开始时 Boss 到目标的水平距离决定。
        /// </summary>
        /// <param name="name">窗口名称，用于运行时状态和调试输出识别当前 CodeMove。</param>
        /// <param name="startTime">窗口在攻击时间轴上的开始时间，单位为秒。</param>
        /// <param name="endTime">窗口在攻击时间轴上的结束时间，单位为秒。</param>
        /// <param name="orbitAngleDegrees">Orbit 在水平面绕目标旋转的角度；右侧为正向，左侧为反向。</param>
        /// <param name="maxMoveSpeed">单帧位移速度上限，单位为米每秒；不限制 Orbit 总移动距离。</param>
        /// <param name="maxYawSpeed">朝向目标的旋转速度上限，单位为度每秒。</param>
        /// <param name="orbitSide">本窗口期望使用的环绕方向；Random 会在窗口开始时随机选择左或右。</param>
        /// <param name="followTarget">为 true 时刷新圆心跟随目标水平位置；不改变窗口开始时锁定的半径。</param>
        /// <returns>配置为 OrbitAroundTarget 的 CodeMove 窗口。</returns>
        public static BossCodeMoveWindow OrbitAroundTarget(
            string name,
            float startTime,
            float endTime,
            float orbitAngleDegrees,
            float maxMoveSpeed,
            float maxYawSpeed,
            BossOrbitSide orbitSide,
            bool followTarget = false)
        {
            return new BossCodeMoveWindow
            {
                Name = name,
                StartTime = startTime,
                EndTime = endTime,
                TargetingMode = BossWarpTargetingMode.OrbitAroundTarget,
                ProgressMode = BossCodeMoveProgressMode.ConstantSpeed,
                StopDistance = 0f,
                DeadZone = 0f,
                MaxMoveDistance = 0f,
                MaxMoveSpeed = Mathf.Max(0f, maxMoveSpeed),
                MaxYawSpeed = Mathf.Max(0f, maxYawSpeed),
                FollowTarget = followTarget,
                AllowBackwardCorrection = true,
                UseDistanceBand = false,
                OrbitSide = orbitSide,
                OrbitAngleDegrees = Mathf.Clamp(orbitAngleDegrees, 0f, 360f)
            };
        }
    }
}
