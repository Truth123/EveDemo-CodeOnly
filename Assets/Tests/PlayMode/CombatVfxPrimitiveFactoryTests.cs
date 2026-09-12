// 文件说明：验证战斗反馈方向线只复用显式材质，并按配置生命周期销毁。
// 所属模块：PlayMode 测试。
// 运行影响：仅在 Unity Test Runner 中创建临时 LineRenderer 和测试材质。

using NUnit.Framework;
using ProjectEVE.Feedback;
using UnityEngine;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class CombatVfxPrimitiveFactoryTests
    {
        [Test]
        public void SpawnDirectionLines_UsesProvidedSharedMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader)
            {
                name = "CombatVfxPrimitiveFactoryTests_Material"
            };

            const string effectName = "CombatVfxPrimitiveFactoryTests_Line";
            try
            {
                CombatVfxPrimitiveFactory.SpawnDirectionLines(
                    effectName,
                    material,
                    Vector3.zero,
                    Vector3.forward,
                    Color.red,
                    1f,
                    0.05f,
                    0.05f,
                    3);

                for (int i = 0; i < 3; i++)
                {
                    GameObject lineObject = GameObject.Find($"{effectName}_{i}");
                    Assert.That(lineObject, Is.Not.Null);
                    Assert.That(lineObject.GetComponent<LineRenderer>().sharedMaterial, Is.SameAs(material));
                }
            }
            finally
            {
                DestroyLines(effectName, 3);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SpawnDirectionLines_WithoutMaterialLogsErrorAndCreatesNothing()
        {
            const string effectName = "CombatVfxPrimitiveFactoryTests_MissingMaterial";
            bool previousLoggingState = Debug.unityLogger.logEnabled;
            try
            {
                Debug.unityLogger.logEnabled = false;
                CombatVfxPrimitiveFactory.SpawnDirectionLines(
                    effectName,
                    null,
                    Vector3.zero,
                    Vector3.forward,
                    Color.red,
                    1f,
                    0.05f,
                    0.1f,
                    3);

                for (int i = 0; i < 3; i++)
                {
                    Assert.That(GameObject.Find($"{effectName}_{i}"), Is.Null);
                }
            }
            finally
            {
                Debug.unityLogger.logEnabled = previousLoggingState;
            }
        }

        /// <summary>清理测试名称前缀下仍然存在的临时方向线。</summary>
        /// <param name="effectName">测试对象名称前缀。</param>
        /// <param name="lineCount">需要检查并清理的方向线数量。</param>
        private static void DestroyLines(string effectName, int lineCount)
        {
            for (int i = 0; i < lineCount; i++)
            {
                GameObject lineObject = GameObject.Find($"{effectName}_{i}");
                if (lineObject != null)
                {
                    Object.DestroyImmediate(lineObject);
                }
            }
        }
    }
}
