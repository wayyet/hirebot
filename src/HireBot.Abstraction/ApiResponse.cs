namespace HireBot.Abstraction;

public record ApiResponse<T>
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; }
    public T? Data { get; set; }

    public ApiResponse(int code, bool success, string message, T? data)
    {
        Code = code;
        Success = success;
        Message = message;
        Data = data;
    }

    public static ApiResponse<T> SuccessResponse(T? data = default, string message = "操作成功")
    {
        return new ApiResponse<T>(200, true, message, data);
    }

    public static ApiResponse<T> ErrorResponse(int code, string message)
    {
        return new ApiResponse<T>(code, false, message, default);
    }
}