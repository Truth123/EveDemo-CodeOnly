// 文件说明：允许项目 EditMode 测试直接验证运行时内部纯逻辑，不扩大正式公共 API。
// 所属模块：Runtime 测试边界。
// 运行影响：只改变编辑器测试程序集对 internal 类型的可见性。

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Assembly-CSharp")]
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
[assembly: InternalsVisibleTo("ProjectEVE.Tests.EditMode")]
[assembly: InternalsVisibleTo("ProjectEVE.Tests.PlayMode")]
