namespace HireBot.Abstraction.Models.Hiring;

/// <summary>
/// 前端上传模板包到雇佣沙箱工作区的结果。
/// </summary>
/// <param name="WorkspaceDir">沙箱内的绝对目录路径，如 "/workspace/uploads/template-packages"。</param>
/// <param name="FileName">已上传的文件名。</param>
/// <param name="WorkspacePath">沙箱内完整文件路径，如 "/workspace/uploads/template-packages/xxx.zip"。</param>
/// <param name="FileMarker">可直接嵌入 WS 消息文本的文件引用标记，格式为 [FILE_URL:path]。</param>
/// <param name="SizeBytes">文件字节数。</param>
public sealed record HiringTemplatePackageUploadResultDto(
    string WorkspaceDir,
    string FileName,
    string WorkspacePath,
    string FileMarker,
    long SizeBytes);
