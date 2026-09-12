// 文件说明：维护 Boss 正式移动请求入口和移动层级仲裁。
// 所属模块：Boss 位移。
// 运行影响：影响 Boss 普通重定位、攻击 / 反应位移生命周期和位移上下文清理。

using ProjectEVE.Boss.AI;
using UnityEngine;

namespace ProjectEVE.Boss.Movement
{
    /// <summary>
    /// Boss 正式移动系统。当前以 BossMotionController 为执行适配层，统一接收 Reposition、ActionMotion 与 ReactionMotion 请求。
    /// </summary>
    public sealed class BossMovementSystem
    {
        private BossMotionController motionController;
        private BossMovementLayer currentLayer = BossMovementLayer.None;
        private string currentActionId = string.Empty;

        /// <summary>当前被移动系统接管的移动层。</summary>
        public BossMovementLayer CurrentLayer => currentLayer;
        /// <summary>当前是否正在执行动作位移。</summary>
        public bool IsActionMotionActive => currentLayer == BossMovementLayer.Action;
        /// <summary>当前是否正在执行反应位移。</summary>
        public bool IsReactionMotionActive => currentLayer == BossMovementLayer.Reaction;
        /// <summary>当前动作或反应位移对应的 Motion Profile Action ID。</summary>
        public string CurrentActionId => currentActionId;

        /// <summary>绑定底层位移执行组件。</summary>
        public void Bind(BossMotionController nextMotionController)
        {
            motionController = nextMotionController;
        }

        /// <summary>更新目标，用于重定位、出招预筛选和攻击位移。</summary>
        public void SetTarget(Transform target)
        {
            motionController?.SetTarget(target);
        }

        /// <summary>只读出招前位移可行性检查，不产生诊断状态或日志。</summary>
        public bool CanStartActionReadOnly(BossAttackDefinition attackDefinition, Transform target)
        {
            return motionController == null || motionController.CanStartAttackReadOnly(attackDefinition, target);
        }

        /// <summary>开始动作位移层，并初始化底层 MotionController 攻击位移上下文。</summary>
        public void BeginActionMotion(BossAttackDefinition attackDefinition, Transform target)
        {
            currentLayer = BossMovementLayer.Action;
            currentActionId = attackDefinition != null ? attackDefinition.AttackId : string.Empty;
            motionController?.BeginAttack(attackDefinition, target);
        }

        /// <summary>推进动作位移层。只有当前处于 Action 层时才会提交给底层 MotionController。</summary>
        public void TickActionMotion(float deltaTime, float attackElapsed)
        {
            if (currentLayer != BossMovementLayer.Action)
            {
                Debug.LogWarning($"BossMovementSystem.TickActionMotion called while currentLayer={currentLayer}. Ignoring.");
                return;
            }

            motionController?.TickAttackMotion(deltaTime, attackElapsed);
        }

        /// <summary>开始反应位移层，并初始化底层 Profile CodeMove 上下文。</summary>
        public bool BeginReactionMotion(string actionId, string profileWindowName, Transform target)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                return false;
            }

            currentLayer = BossMovementLayer.Reaction;
            currentActionId = actionId;
            bool started = motionController != null &&
                motionController.BeginReactionMotion(actionId, profileWindowName, target);
            if (!started)
            {
                motionController?.EndReactionMotion();
                currentLayer = BossMovementLayer.None;
                currentActionId = string.Empty;
            }

            return started;
        }

        /// <summary>推进反应位移层。只有当前处于 Reaction 层时才会提交给底层 MotionController。</summary>
        public void TickReactionMotion(float deltaTime, float reactionElapsed)
        {
            if (currentLayer != BossMovementLayer.Reaction)
            {
                return;
            }

            motionController?.TickReactionMotion(deltaTime, reactionElapsed);
        }

        /// <summary>处理 Animator Root Motion。只有 Action 层激活时才允许提交到底层 MotionController。</summary>
        public void HandleRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            if (currentLayer != BossMovementLayer.Action)
            {
                return;
            }

            motionController?.HandleRootMotion(deltaPosition, deltaRotation);
        }

        /// <summary>结束动作位移层，并清理底层 MotionController 攻击位移上下文。</summary>
        public void EndActionMotion()
        {
            if (currentLayer != BossMovementLayer.Action)
            {
                return;
            }

            motionController?.EndAttack();
            currentLayer = BossMovementLayer.None;
            currentActionId = string.Empty;
        }

        /// <summary>结束反应位移层。当前层已经切到 Action 或 Reposition 时不清理新层级。</summary>
        public void EndReactionMotion()
        {
            if (currentLayer != BossMovementLayer.Reaction)
            {
                return;
            }

            motionController?.EndReactionMotion();
            currentLayer = BossMovementLayer.None;
            currentActionId = string.Empty;
        }

        /// <summary>由 Reposition 层提交普通代码位移。已承诺动作或反应层激活时拒绝普通移动覆盖。</summary>
        public Vector3 MoveReposition(Vector3 direction, float speed, float deltaTime)
        {
            if (currentLayer == BossMovementLayer.Action ||
                currentLayer == BossMovementLayer.Reaction)
            {
                return Vector3.zero;
            }

            currentLayer = BossMovementLayer.Reposition;
            return motionController != null
                ? motionController.MoveCodeDriven(direction, speed, deltaTime)
                : Vector3.zero;
        }

        /// <summary>结束 Reposition 层。不会影响正在执行的 Action 层。</summary>
        public void EndReposition()
        {
            if (currentLayer == BossMovementLayer.Reposition)
            {
                currentLayer = BossMovementLayer.None;
            }
        }

        /// <summary>因受击、倒地、死亡或停止 AI 清理所有移动上下文。</summary>
        public void StopAllMotion()
        {
            if (currentLayer == BossMovementLayer.Action)
            {
                motionController?.EndAttack();
            }
            else if (currentLayer == BossMovementLayer.Reaction)
            {
                motionController?.EndReactionMotion();
            }

            currentLayer = BossMovementLayer.None;
            currentActionId = string.Empty;
        }
    }

    /// <summary>
    /// Boss 当前移动请求层级。用于防止重定位移动覆盖已承诺动作位移。
    /// </summary>
    public enum BossMovementLayer
    {
        None = 0,
        Reposition = 1,
        Action = 2,
        Reaction = 3
    }
}
