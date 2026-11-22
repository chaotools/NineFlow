namespace NineFlow.Core;

/// <summary>按键发送抽象，便于测试时替换为假实现。</summary>
public interface IKeySender
{
    /// <summary>发送一组按键动作（每个动作按下并释放）。</summary>
    bool Send(IReadOnlyList<SendToken> tokens, out string error);
}
