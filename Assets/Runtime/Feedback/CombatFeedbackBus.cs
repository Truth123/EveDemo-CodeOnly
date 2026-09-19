// 文件说明：维护 HitStop、CameraShake、VFX/SFX 事件和测试触发。
// 所属模块：战斗反馈。
// 运行影响：影响命中反馈播放、调试触发和反馈事件传递。

using System;

namespace ProjectEVE.Feedback
{
    /// <summary>
    /// 轻量战斗反馈分发器。战斗结算只发请求，表现层自行订阅播放。
    /// </summary>
    public static class CombatFeedbackBus
    {
        public static event Action<CombatFeedbackEvent> FeedbackRequested;


        public static void Raise(in CombatFeedbackEvent feedbackEvent)
        {
            if (feedbackEvent.Kind == CombatFeedbackKind.None)
            {
                return;
            }

            FeedbackRequested?.Invoke(feedbackEvent);
        }
    }
}
