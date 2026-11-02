// Ví dụ console: liệt kê chứng thư, băm một file bằng SHA-256 rồi ký mã băm qua MySign, in chữ ký base64.
// Cấu hình (ClientId, ClientSecret, ProfileId, UserId) đọc từ user-secrets hoặc biến môi trường, KHÔNG ghi vào file:
//   dotnet user-secrets set "Viettel:MySign:ClientId" "..." --project samples/ViettelConnect.MySign.Sample.Console
//   hoặc biến môi trường Viettel__MySign__ClientId, Viettel__MySign__ClientSecret, Viettel__MySign__ProfileId, Viettel__MySign__UserId
// Chạy: dotnet run --project samples/ViettelConnect.MySign.Sample.Console -- <đường-dẫn-file>
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ViettelConnect.MySign.Client;
using ViettelConnect.MySign.Client.DependencyInjection;
using ViettelConnect.MySign.Client.Dtos;
using ViettelConnect.MySign.Domain.Errors;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Cách dùng: ViettelConnect.MySign.Sample.Console <đường-dẫn-file-cần-ký>");
    return 2;
}

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole());
services.AddViettelMySign(config);

using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();

try
{
    Console.WriteLine("Chứng thư của người ký:");
    var certificates = await client.ListCertificatesAsync();
    foreach (var cert in certificates)
    {
        Console.WriteLine($"  {cert.CredentialId} | {cert.SubjectDn} | hiệu lực đến {cert.ValidTo:yyyy-MM-dd} | khoá bật: {cert.KeyEnabled}");
    }

    var hash = SHA256.HashData(await File.ReadAllBytesAsync(args[0]));
    var request = new SignHashesRequestDto(
        [new HashDocumentDto("doc-1", Path.GetFileName(args[0]), hash)],
        Description: "Ví dụ ký mã băm");

    Console.WriteLine("Đã gửi yêu cầu ký. Hãy xác nhận trên ứng dụng MySign...");
    var result = await client.SignHashesAsync(request);
    Console.WriteLine($"Chứng thư đã ký: {result.Certificate.CredentialId}");
    foreach (var signature in result.Signatures)
    {
        Console.WriteLine($"{signature.DocumentId}: {Convert.ToBase64String(signature.Signature)}");
    }

    return 0;
}
catch (MySignTimeoutException ex)
{
    // Ném ở cả chế độ Synchronous lẫn Polling.
    Console.Error.WriteLine($"Người ký không xác nhận kịp (giao dịch: {ex.TransactionId ?? "-"}, correlation: {ex.CorrelationId}).");
    return 1;
}
catch (MySignException ex)
{
    // Không in thông điệp lỗi thô của Viettel (chỉ hiện khi bật Errors.IncludeRawErrorMessage).
    // TransactionId khác null nghĩa là Viettel đã nhận yêu cầu: đừng tự gửi lại, hãy đối soát theo mã giao dịch.
    Console.Error.WriteLine($"Lỗi MySign [{ex.Category}] {ex.Detail} (giao dịch: {ex.TransactionId ?? "-"}, correlation: {ex.CorrelationId})");
    return 1;
}

public partial class Program;
