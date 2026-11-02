using System.Net.Http.Headers;
using System.Net.Http.Json;
using PineAI.Core.Entities;
using PineAI.Core.Features.Account;
using PineAI.Core.Features.ApiKey;
using PineAI.Core.Features.BotConversation;
using PineAI.Core.Features.Customer;
using PineAI.Core.Features.MenuLink;
using PineAI.Core.Features.Order;
using PineAI.Core.Features.Safir;
using PineAI.Core.Features.Sms;

namespace PineAI.UI.Services;

public class ApiClientService
{
    private readonly HttpClient httpClient;
    private string? token;

    public ApiClientService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public void SetToken(string jwtToken)
    {
        token = jwtToken;
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
    }

    public void ClearToken()
    {
        token = null;
        httpClient.DefaultRequestHeaders.Authorization = null;
    }

    public bool HasToken => !string.IsNullOrEmpty(token);

    public async Task<GetUserLoginResult?> LoginAsync(GetUserLoginQuery query)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("api/auth/login", query);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GetUserLoginResult>();
                return result ?? new GetUserLoginResult { Success = false, Message = "خطا در خواندن پاسخ سرور" };
            }

            return new GetUserLoginResult { Success = false, Message = "خطا در ورود به سیستم" };
        }
        catch (HttpRequestException ex)
        {
            return new GetUserLoginResult { Success = false, Message = $"خطای شبکه: {ex.Message}" };
        }
        catch (TaskCanceledException)
        {
            return new GetUserLoginResult { Success = false, Message = "زمان درخواست به پایان رسید" };
        }
        catch (Exception ex)
        {
            return new GetUserLoginResult { Success = false, Message = $"خطای غیرمنتظره: {ex.Message}" };
        }
    }

    public async Task<InsertCustomerResult> InsertCustomerAsync(InsertCustomerCommand command)
    {
        var response = await httpClient.PostAsJsonAsync("api/customer", command);
        if (response.IsSuccessStatusCode)
            return new InsertCustomerResult { Success = true, Message = "مشتری با موفقیت ثبت شد" };
        var error = await response.Content.ReadFromJsonAsync<MessageResponse>();
        return new InsertCustomerResult { Success = false, Message = error?.Message ?? "خطا در ثبت مشتری" };
    }

    public async Task<ImportCustomersResult?> ImportCustomersAsync(ImportCustomersCommand command)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("api/customer/import", command);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<ImportCustomersResult>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<PineAI.Core.Entities.Customer>?> GetCustomersByRangeAsync(DateTime from, DateTime to, string? phonePrefix = null, bool? isTester = null)
    {
        try
        {
            var url = $"api/customer/byrange?from={from:yyyy-MM-ddTHH:mm:ss}&to={to:yyyy-MM-ddTHH:mm:ss}";
            if (!string.IsNullOrEmpty(phonePrefix))
                url += $"&phonePrefix={Uri.EscapeDataString(phonePrefix)}";
            if (isTester.HasValue)
                url += $"&isTester={isTester.Value.ToString().ToLowerInvariant()}";
            return await httpClient.GetFromJsonAsync<List<PineAI.Core.Entities.Customer>>(url);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetCustomerByPhoneResult> GetCustomerByPhoneAsync(string phoneNumber)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/customer/byphone/{phoneNumber}");
            if (response.IsSuccessStatusCode)
            {
                var customer = await response.Content.ReadFromJsonAsync<PineAI.Core.Entities.Customer>();
                return new GetCustomerByPhoneResult { Customer = customer };
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new GetCustomerByPhoneResult { ErrorMessage = "مشتری با این شماره یافت نشد" };
            return new GetCustomerByPhoneResult { ErrorMessage = "خطا در جستجو" };
        }
        catch (HttpRequestException ex)
        {
            return new GetCustomerByPhoneResult { ErrorMessage = $"خطای شبکه: {ex.Message}" };
        }
        catch (TaskCanceledException)
        {
            return new GetCustomerByPhoneResult { ErrorMessage = "زمان درخواست به پایان رسید" };
        }
        catch (Exception ex)
        {
            return new GetCustomerByPhoneResult { ErrorMessage = $"خطا: {ex.Message}" };
        }
    }

    public async Task<UpdateCustomerResult> UpdateCustomerAsync(UpdateCustomerCommand command)
    {
        var response = await httpClient.PutAsJsonAsync($"api/customer/{command.Id}", command);
        if (response.IsSuccessStatusCode)
        {
            var ok = await response.Content.ReadFromJsonAsync<MessageResponse>();
            return new UpdateCustomerResult { Success = true, Message = ok?.Message ?? "اطلاعات مشتری به‌روزرسانی شد" };
        }
        var error = await response.Content.ReadFromJsonAsync<MessageResponse>();
        return new UpdateCustomerResult { Success = false, Message = error?.Message ?? "خطا در به‌روزرسانی مشتری" };
    }

    public async Task<SendSmsResult?> SendSmsAsync(SendSmsCommand command)
    {
        var response = await httpClient.PostAsJsonAsync("api/sms/send", command);
        return await response.Content.ReadFromJsonAsync<SendSmsResult>();
    }

    public async Task<ScheduleSmsResult?> ScheduleSmsAsync(ScheduleSmsCommand command)
    {
        var response = await httpClient.PostAsJsonAsync("api/sms/schedule", command);
        return await response.Content.ReadFromJsonAsync<ScheduleSmsResult>();
    }

    public async Task<List<SmsSendJobDto>?> GetSmsJobsAsync()
    {
        return await httpClient.GetFromJsonAsync<List<SmsSendJobDto>>("api/sms/jobs");
    }

    public async Task<SmsSendJobDto?> GetSmsJobAsync(int id)
    {
        return await httpClient.GetFromJsonAsync<SmsSendJobDto>($"api/sms/jobs/{id}");
    }

    public async Task<BulkUpdateTrackingResult?> BulkUpdateTrackingAsync(BulkUpdateTrackingCommand command)
    {
        var response = await httpClient.PostAsJsonAsync("api/order/bulk-tracking", command);
        return await response.Content.ReadFromJsonAsync<BulkUpdateTrackingResult>();
    }

    // ── Order Statuses ──────────────────────────────────────────────────────
    public async Task<List<OrderStatus>?> GetOrderStatusesAsync()
    {
        return await httpClient.GetFromJsonAsync<List<OrderStatus>>("api/order/statuses");
    }

    public async Task<UpsertOrderStatusResult> UpsertOrderStatusAsync(UpsertOrderStatusCommand command)
    {
        var response = await httpClient.PostAsJsonAsync("api/order/statuses", command);
        if (response.IsSuccessStatusCode)
        {
            var ok = await response.Content.ReadFromJsonAsync<MessageResponse>();
            return new UpsertOrderStatusResult { Success = true, Message = ok?.Message ?? "ذخیره شد" };
        }
        var error = await response.Content.ReadFromJsonAsync<MessageResponse>();
        return new UpsertOrderStatusResult { Success = false, Message = error?.Message ?? "خطا در ذخیره‌سازی" };
    }

    public async Task<DeleteOrderStatusResult> DeleteOrderStatusAsync(int id)
    {
        var response = await httpClient.DeleteAsync($"api/order/statuses/{id}");
        if (response.IsSuccessStatusCode)
        {
            var ok = await response.Content.ReadFromJsonAsync<MessageResponse>();
            return new DeleteOrderStatusResult { Success = true, Message = ok?.Message ?? "حذف شد" };
        }
        var error = await response.Content.ReadFromJsonAsync<MessageResponse>();
        return new DeleteOrderStatusResult { Success = false, Message = error?.Message ?? "خطا در حذف" };
    }

    // ── API Keys ─────────────────────────────────────────────────────────────
    public async Task<List<ApiKey>?> GetApiKeysAsync()
    {
        return await httpClient.GetFromJsonAsync<List<ApiKey>>("api/apikey");
    }

    public async Task<CreateApiKeyResult?> CreateApiKeyAsync(CreateApiKeyCommand command)
    {
        var response = await httpClient.PostAsJsonAsync("api/apikey", command);
        return await response.Content.ReadFromJsonAsync<CreateApiKeyResult>();
    }

    public async Task<DeleteApiKeyResult> DeleteApiKeyAsync(int id)
    {
        var response = await httpClient.DeleteAsync($"api/apikey/{id}");
        if (response.IsSuccessStatusCode)
        {
            var ok = await response.Content.ReadFromJsonAsync<MessageResponse>();
            return new DeleteApiKeyResult { Success = true, Message = ok?.Message ?? "حذف شد" };
        }
        var error = await response.Content.ReadFromJsonAsync<MessageResponse>();
        return new DeleteApiKeyResult { Success = false, Message = error?.Message ?? "خطا در حذف" };
    }

    // ── Bot Conversations ──────────────────────────────────────────────────
    public async Task<List<BotChatMessageDto>?> GetBotConversationAsync(string username)
    {
        return await httpClient.GetFromJsonAsync<List<BotChatMessageDto>>(
            $"api/bot/conversations?username={Uri.EscapeDataString(username)}");
    }

    public async Task<BotUserSummaryPageResult?> GetBotUserSummariesAsync(int page, int pageSize = 10)
    {
        return await httpClient.GetFromJsonAsync<BotUserSummaryPageResult>(
            $"api/bot/user-summaries?page={page}&pageSize={pageSize}");
    }

    // ── Menu Links ────────────────────────────────────────────────────────
    public async Task<List<MenuLinkDto>?> GetMyMenuLinksAsync()
    {
        return await httpClient.GetFromJsonAsync<List<MenuLinkDto>>("api/menulink");
    }

    public async Task<List<MenuLinkDto>?> GetAllMenuLinksAsync()
    {
        return await httpClient.GetFromJsonAsync<List<MenuLinkDto>>("api/menulink/all");
    }

    public async Task<List<UserDto>?> GetAllUsersAsync()
    {
        return await httpClient.GetFromJsonAsync<List<UserDto>>("api/menulink/users");
    }

    public async Task<List<int>?> GetUserMenuLinkIdsAsync(string userId)
    {
        return await httpClient.GetFromJsonAsync<List<int>>($"api/menulink/users/{userId}/links");
    }

    public async Task<bool> SaveUserMenuLinksAsync(string userId, List<int> menuLinkIds)
    {
        var command = new SaveUserMenuLinksCommand { UserId = userId, MenuLinkIds = menuLinkIds };
        var response = await httpClient.PostAsJsonAsync($"api/menulink/users/{userId}/links", command);
        return response.IsSuccessStatusCode;
    }

    // ── User Management ───────────────────────────────────────────────────
    public async Task<List<UserDto>?> GetNonAdminUsersAsync()
    {
        return await httpClient.GetFromJsonAsync<List<UserDto>>("api/user");
    }

    public async Task<CreateUserResult> CreateUserAsync(CreateUserCommand command)
    {
        var response = await httpClient.PostAsJsonAsync("api/user", command);
        var result = await response.Content.ReadFromJsonAsync<MessageResponse>();
        return new CreateUserResult
        {
            Success = response.IsSuccessStatusCode,
            Message = result?.Message ?? (response.IsSuccessStatusCode ? "کاربر ایجاد شد" : "خطا در ایجاد کاربر")
        };
    }

    public async Task<UpdateUserResult> UpdateUserAsync(string userId, UpdateUserCommand command)
    {
        var response = await httpClient.PutAsJsonAsync($"api/user/{userId}", command);
        var result = await response.Content.ReadFromJsonAsync<MessageResponse>();
        return new UpdateUserResult
        {
            Success = response.IsSuccessStatusCode,
            Message = result?.Message ?? (response.IsSuccessStatusCode ? "کاربر به‌روزرسانی شد" : "خطا در به‌روزرسانی کاربر")
        };
    }

    public async Task<DeleteUserResult> DeleteUserAsync(string userId)
    {
        var response = await httpClient.DeleteAsync($"api/user/{userId}");
        var result = await response.Content.ReadFromJsonAsync<MessageResponse>();
        return new DeleteUserResult
        {
            Success = response.IsSuccessStatusCode,
            Message = result?.Message ?? (response.IsSuccessStatusCode ? "کاربر حذف شد" : "خطا در حذف کاربر")
        };
    }

    // ── Order Statistics ────────────────────────────────────────────────
    public async Task<OrderStatisticsResult?> GetOrderStatisticsAsync(DateTime startDate, DateTime endDate, string groupBy = "day")
    {
        try
        {
            var url = $"api/order/statistics?startDate={startDate:yyyy-MM-ddTHH:mm:ss}&endDate={endDate:yyyy-MM-ddTHH:mm:ss}&groupBy={groupBy}";
            return await httpClient.GetFromJsonAsync<OrderStatisticsResult>(url);
        }
        catch
        {
            return null;
        }
    }

    // ── Safir broadcast (message to all customers) ─────────────────────────
    public async Task<SafirRecipientsInfoDto?> GetSafirRecipientsAsync()
    {
        try
        {
            return await httpClient.GetFromJsonAsync<SafirRecipientsInfoDto>("api/safir/recipients");
        }
        catch
        {
            return null;
        }
    }

    public async Task<StartSafirBroadcastResult> StartSafirBroadcastAsync(string? message, IEnumerable<SafirImageUpload> images)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(message ?? string.Empty), "message");

            foreach (var image in images)
            {
                var content = new ByteArrayContent(image.Content);
                content.Headers.ContentType = MediaTypeHeaderValue.TryParse(image.ContentType, out var mediaType)
                    ? mediaType
                    : new MediaTypeHeaderValue("application/octet-stream");
                form.Add(content, "images", image.FileName);
            }

            var response = await httpClient.PostAsync("api/safir/broadcast", form);
            if (response.IsSuccessStatusCode)
            {
                var ok = await response.Content.ReadFromJsonAsync<StartSafirBroadcastResult>();
                return ok ?? new StartSafirBroadcastResult { Success = false, Message = "خطا در خواندن پاسخ سرور" };
            }

            return new StartSafirBroadcastResult
            {
                Success = false,
                Message = await ReadErrorMessageAsync(response, "خطا در شروع ارسال")
            };
        }
        catch (TaskCanceledException)
        {
            return new StartSafirBroadcastResult { Success = false, Message = "زمان درخواست به پایان رسید" };
        }
        catch (Exception ex)
        {
            return new StartSafirBroadcastResult { Success = false, Message = $"خطا در اتصال به سرور: {ex.Message}" };
        }
    }

    public async Task<SafirBroadcastStatusDto?> GetSafirBroadcastAsync(string jobId)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<SafirBroadcastStatusDto>(
                $"api/safir/broadcast/{Uri.EscapeDataString(jobId)}");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Latest broadcast job, or null when none exists (204) or the call failed.</summary>
    public async Task<SafirBroadcastStatusDto?> GetLatestSafirBroadcastAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/safir/broadcast/latest");
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent || !response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<SafirBroadcastStatusDto>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> CancelSafirBroadcastAsync(string jobId)
    {
        try
        {
            var response = await httpClient.PostAsync($"api/safir/broadcast/{Uri.EscapeDataString(jobId)}/cancel", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Broadcasts that stopped before reaching everyone and can be resumed.</summary>
    public async Task<List<SafirBroadcastStatusDto>> GetUnfinishedSafirBroadcastsAsync()
    {
        try
        {
            return await httpClient.GetFromJsonAsync<List<SafirBroadcastStatusDto>>("api/safir/broadcast/unfinished")
                   ?? new List<SafirBroadcastStatusDto>();
        }
        catch
        {
            return new List<SafirBroadcastStatusDto>();
        }
    }

    /// <summary>Continues an unfinished broadcast. Returns the job status on success, otherwise an error message.</summary>
    public async Task<(SafirBroadcastStatusDto? Status, string? Error)> ResumeSafirBroadcastAsync(string jobId)
    {
        try
        {
            var response = await httpClient.PostAsync($"api/safir/broadcast/{Uri.EscapeDataString(jobId)}/resume", null);
            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<SafirBroadcastStatusDto>(), null);

            return (null, await ReadErrorMessageAsync(response, "ادامه ارسال انجام نشد"));
        }
        catch (Exception ex)
        {
            return (null, $"خطا در اتصال به سرور: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> DeleteSafirBroadcastAsync(string jobId)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/safir/broadcast/{Uri.EscapeDataString(jobId)}");
            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, await ReadErrorMessageAsync(response, "حذف انجام نشد"));
        }
        catch (Exception ex)
        {
            return (false, $"خطا در اتصال به سرور: {ex.Message}");
        }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<MessageResponse>();
            if (!string.IsNullOrWhiteSpace(error?.Message))
                return error.Message;
        }
        catch
        {
            // body was empty or not JSON (e.g. 413 from the web server)
        }

        return response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge
            ? "حجم فایل‌ها بیش از حد مجاز است"
            : fallback;
    }
}

public class MessageResponse
{
    public string Message { get; set; } = string.Empty;
}
