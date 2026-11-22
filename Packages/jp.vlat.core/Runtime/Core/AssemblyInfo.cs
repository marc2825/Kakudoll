using System.Runtime.CompilerServices;

// 保存中断（異常終了）を模擬する試験用フックを、EditMode試験からだけ使えるようにする。
[assembly: InternalsVisibleTo("VLat.Tests.Edit")]
