using PineAI.Core.Entities;
using PineAI.Core.Features.Customer;

namespace PineAI.Core.Contracts;

public interface ICustomerService
{
    Task<InsertCustomerResult> InsertCustomer(InsertCustomerCommand command, string userId);
    Task<ImportCustomersResult> ImportCustomers(ImportCustomersCommand command, string userId);
    Task<List<Customer>> GetCustomersByDateRange(DateTime from, DateTime to, string? phonePrefix = null, bool? isTester = null);
    Task<GetCustomerByPhoneResult> GetCustomerByPhoneNumber(string phoneNumber);
    Task<UpdateCustomerResult> UpdateCustomer(UpdateCustomerCommand command);

    /// <summary>Returns every distinct, non-empty customer phone number (10 digits, no country code).</summary>
    Task<List<string>> GetDistinctPhoneNumbers();

    /// <summary>Returns every distinct, non-empty phone number of customers flagged IsTester.</summary>
    Task<List<string>> GetTesterPhoneNumbers();
}
