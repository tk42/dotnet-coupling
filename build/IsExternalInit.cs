// net472 には System.Runtime.CompilerServices.IsExternalInit が無い。
// record / init アクセサを使うためのコンパイル時 shim（実体は不要）。
// 各アセンブリに internal として注入される（docs/implementation-plan.md §0 net472 注意）。

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif
