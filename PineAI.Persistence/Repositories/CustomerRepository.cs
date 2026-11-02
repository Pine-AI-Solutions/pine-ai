using Microsoft.EntityFrameworkCore;
using PineAI.Core.Contracts;
using PineAI.Core.Entities;
using PineAI.Core.Features.Customer;
using PineAI.Persistence.Services;

namespace PineAI.Persistence.Repositories;

public class CustomerRepository : ICustomerService
{
    private readonly PineAIDbContext dbContext;

    public CustomerRepository(PineAIDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<InsertCustomerResult> InsertCustomer(InsertCustomerCommand command, string userId)
    {
        if (!IsValidPhoneNumber(command.PhoneNumber))
            return new InsertCustomerResult { Success = false, Message = "شماره موبایل معتبر نیست" };

        bool exists = await dbContext.Customer.AnyAsync(c => c.PhoneNumber == command.PhoneNumber);
        if (exists)
            return new InsertCustomerResult { Success = false, Message = "این شماره موبایل قبلاً ثبت شده است" };

        DateTime? birthDate = null;
        if (!string.IsNullOrEmpty(command.BirthDate))
        {
            var pc = new System.Globalization.PersianCalendar();
            var parts = command.BirthDate.Split('/');
            if (parts.Length == 3 && int.TryParse(parts[0], out int y) && int.TryParse(parts[1], out int m) && int.TryParse(parts[2], out int d))
                birthDate = pc.ToDateTime(y, m, d, 0, 0, 0, 0);
        }

        var customer = new Customer
        {
            PhoneNumber = command.PhoneNumber,
            Name = command.Name,
            Gender = command.Gender,
            BirthYear = command.BirthYear,
            BirthDate = birthDate,
            SaveDate = DateTime.Now,
            SaveUserId = userId,
            SaveType = 1,
            IsTester = command.IsTester
        };

        dbContext.Customer.Add(customer);
        await dbContext.SaveChangesAsync();
        return new InsertCustomerResult { Success = true, Message = "مشتری با موفقیت ثبت شد" };
    }

    public async Task<ImportCustomersResult> ImportCustomers(ImportCustomersCommand command, string userId)
    {
        var result = new ImportCustomersResult();
        var validNumbers = new List<string>();
        var invalidNumbers = new List<string>();
        var duplicateNumbers = new List<string>();

        foreach (var phone in command.PhoneNumbers)
        {
            if (!IsValidPhoneNumber(phone))
                invalidNumbers.Add(phone);
            else
                validNumbers.Add(phone);
        }

        var distinctValid = validNumbers.Distinct().ToList();
        var internalDuplicates = validNumbers.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var dup in internalDuplicates)
            if (!duplicateNumbers.Contains(dup))
                duplicateNumbers.Add(dup);

        var existingNumbers = await dbContext.Customer
            .Where(c => distinctValid.Contains(c.PhoneNumber))
            .Select(c => c.PhoneNumber)
            .ToListAsync();
        
        foreach (var existing in existingNumbers)
            if (!duplicateNumbers.Contains(existing))
                duplicateNumbers.Add(existing);

        result.InvalidNumbers = invalidNumbers;
        result.DuplicateNumbers = duplicateNumbers;

        bool hasIssues = invalidNumbers.Count > 0 || duplicateNumbers.Count > 0;

        if (hasIssues && !command.IgnoreInvalid)
        {
            result.Success = false;
            result.Message = "اعداد نامعتبر یا تکراری وجود دارد. برای ادامه تأیید کنید.";
            return result;
        }

        var toInsert = distinctValid.Except(existingNumbers).ToList();
        
        DateTime saveDate = FromPersianDate(command.SaveDate) ?? DateTime.Now;
        
        foreach (var phone in toInsert)
        {
            dbContext.Customer.Add(new Customer
            {
                PhoneNumber = phone,
                SaveDate = saveDate,
                SaveUserId = userId,
                SaveType = 2
            });
        }

        var duplicateEntities = await dbContext.Customer
            .Where(c => existingNumbers.Contains(c.PhoneNumber))
            .ToListAsync();
        
        foreach (var entity in duplicateEntities)
            entity.LastUsageDate = DateTime.Now;

        await dbContext.SaveChangesAsync();
        
        result.Success = true;
        result.InsertedCount = toInsert.Count;
        result.Message = $"{toInsert.Count} شماره با موفقیت ثبت شد";
        return result;
    }

    public async Task<List<Customer>> GetCustomersByDateRange(DateTime from, DateTime to, string? phonePrefix = null, bool? isTester = null)
    {
        var query = dbContext.Customer
            .Where(c => c.SaveDate >= from && c.SaveDate <= to);

        if (!string.IsNullOrEmpty(phonePrefix))
            query = query.Where(c => c.PhoneNumber.StartsWith(phonePrefix));

        if (isTester.HasValue)
            query = query.Where(c => c.IsTester == isTester.Value);

        return await query
            .OrderBy(c => c.PhoneNumber)
            .ToListAsync();
    }

    public async Task<GetCustomerByPhoneResult> GetCustomerByPhoneNumber(string phoneNumber)
    {
        var customer = await dbContext.Customer
            .FirstOrDefaultAsync(c => c.PhoneNumber == phoneNumber);

        if (customer == null)
            return new GetCustomerByPhoneResult { Customer = null, ErrorMessage = "مشتری با این شماره یافت نشد" };

        return new GetCustomerByPhoneResult { Customer = customer, ErrorMessage = null };
    }

    public async Task<UpdateCustomerResult> UpdateCustomer(UpdateCustomerCommand command)
    {
        var customer = await dbContext.Customer.FindAsync(command.Id);
        if (customer == null)
            return new UpdateCustomerResult { Success = false, Message = "مشتری یافت نشد" };

        customer.Name = command.Name;
        customer.Gender = command.Gender;
        customer.BirthYear = command.BirthYear;
        customer.IsTester = command.IsTester;

        if (!string.IsNullOrEmpty(command.BirthDate))
        {
            var pc = new System.Globalization.PersianCalendar();
            var parts = command.BirthDate.Split('/');
            if (parts.Length == 3 && int.TryParse(parts[0], out int y) && int.TryParse(parts[1], out int m) && int.TryParse(parts[2], out int d))
                customer.BirthDate = pc.ToDateTime(y, m, d, 0, 0, 0, 0);
        }
        else
        {
            customer.BirthDate = null;
        }

        await dbContext.SaveChangesAsync();
        return new UpdateCustomerResult { Success = true, Message = "اطلاعات مشتری با موفقیت به‌روزرسانی شد" };
    }

    public async Task<List<string>> GetDistinctPhoneNumbers()
    {
        return await dbContext.Customer
            .AsNoTracking()
            .Where(c => c.PhoneNumber != null && c.PhoneNumber != string.Empty)
            .Select(c => c.PhoneNumber)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync();
    }

    public async Task<List<string>> GetTesterPhoneNumbers()
    {
        return await dbContext.Customer
            .AsNoTracking()
            .Where(c => c.IsTester && c.PhoneNumber != null && c.PhoneNumber != string.Empty)
            .Select(c => c.PhoneNumber)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync();
    }

    private static bool IsValidPhoneNumber(string phone)
    {
        return !string.IsNullOrEmpty(phone) 
            && phone.Length == 10 
            && phone.All(char.IsDigit) 
            && phone.StartsWith("9");
    }

    /// <summary>Converts a Persian date string (yyyy/MM/dd) to UTC DateTime. Returns null on failure.</summary>
    private static DateTime? FromPersianDate(string? persianDate)
    {
        if (string.IsNullOrWhiteSpace(persianDate)) return null;
        var parts = persianDate.Split('/');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out int y) &&
            int.TryParse(parts[1], out int m) &&
            int.TryParse(parts[2], out int d))
        {
            try { return new System.Globalization.PersianCalendar().ToDateTime(y, m, d, 0, 0, 0, 0); }
            catch { return null; }
        }
        return null;
    }
}
