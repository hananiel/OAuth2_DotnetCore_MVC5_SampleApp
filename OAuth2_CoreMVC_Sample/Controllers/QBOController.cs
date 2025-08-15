using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Intuit.Ipp.Core;
using Intuit.Ipp.Data;
using Intuit.Ipp.DataService;
using Intuit.Ipp.QueryFilter;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OAuth2_CoreMVC_Sample.Helper;
using OAuth2_CoreMVC_Sample.Models;


// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace OAuth2_CoreMVC_Sample.Controllers
{
    public class QBOController : Controller
    {
        private readonly IServices _services;
        private string id;
        private string txnId;
        private string pymtId; 
        
        private readonly string _webhookVerifierToken = "8a8cb934-685c-4091-80f1-ea4414d77544"; // Replace with your token from Intuit Developer Portal
        private readonly WebhookDataStore _webhookDataStore;
        private readonly TokensContext _tokens;
        private readonly OAuth2Keys _authKeys;
        private readonly HttpClient _httpClient;

        public QBOController(IServices services, 
            WebhookDataStore webhookDataStore,
            TokensContext tokens,
            IOptions<OAuth2Keys> auth2Keys,
            IHttpClientFactory httpClientFactory)
        {
            _services = services;
            _webhookDataStore = webhookDataStore;
            _tokens = tokens;
            _authKeys =  auth2Keys.Value;
            _httpClient = httpClientFactory.CreateClient();
        }

        // GET: /<controller>/
        public IActionResult Index()
        {
            var webhookInfo = _webhookDataStore.GetAllInfo();
            var tokensList = _tokens.Token.ToList();
            // Token info as a list for display with revoke links
            var tokensInfo = new List<(string RealmId, string TokenSummary)>();
            foreach (var entry in tokensList)
            {
                var tokenVal = entry.RefreshToken.Substring(0, 10) + "...";
                 tokensInfo.Add((entry.RealmId, tokenVal));
            }
            ViewData["TokensInfo"] = tokensInfo;
            ViewData["WebhookInfo"] = webhookInfo.Length > 0 ? webhookInfo : "No webhook data received yet.";
            
            return View("QBO");
        }

        [HttpGet]
        public async Task<IActionResult> RevokeMerchant(string realmId)
        {
            if (string.IsNullOrEmpty(realmId))
            {
                ViewData["TokensInfo"] = new List<(string, string)> { ("N/A", "Error: RealmId is required.") };
                return View("Index");
            }

            var currentToken = _tokens.Token.FirstOrDefault(t => t.RealmId == realmId);
            if (string.IsNullOrEmpty(currentToken.RefreshToken))
            {
                ViewData["TokensInfo"] = new List<(string, string)> { (realmId, "No refresh token found.") };
                return View("Index");
            }

            var authBytes = Encoding.ASCII.GetBytes($"{_authKeys.ClientId}:{_authKeys.ClientSecret}");
            var authHeader = Convert.ToBase64String(authBytes);
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://oauth.platform.intuit.com/oauth2/v1/tokens/revoke");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("token", currentToken.RefreshToken)
            });

            var response = await _httpClient.SendAsync(request);


            if (response.IsSuccessStatusCode)
            {
                _tokens.Token.Remove(currentToken);
                await _tokens.SaveChangesAsync();
            }
            else
            {
                throw new Exception(await response.Content.ReadAsStringAsync());
            }

            return RedirectToAction("Index");

        }

        [HttpGet]
        public IActionResult PrepareWebhookRegistration()
        {
            // Generate webhook URL (use ngrok for local dev, or your production domain)
            var baseUrl = $"{Request.Scheme}://{Request.Host}"; // e.g., https://localhost:5001
            var webhookUrl = $"{baseUrl}/QBO/Webhook";

            // Instructions for manual setup
            var info = $"<p>Your app's webhook URL: <strong>{webhookUrl}</strong> (copy this).</p>" +
                       "<p>Register this URL in the Intuit Developer Portal to receive events for all connected companies (e.g., company1):</p>" +
                       "<ol>" +
                       "<li>Go to <a href='https://developer.intuit.com/' target='_blank'>Intuit Developer Portal</a>.</li>" +
                       "<li>Select your app > 'Development' tab > 'Webhooks' section.</li>" +
                       "<li>Paste the URL above into 'Endpoint URL'.</li>" +
                       "<li>Select 'Customer' > 'Create' event.</li>" +
                       "<li>Save and note the verifier token for your code.</li>" +
                       "<li>Test with 'Send Test Notification' in the portal.</li>" +
                       "</ol>" +
                       "<p>Events from all companies (e.g., company1) will hit this URL. The payload includes the realmId to identify the company.</p>";

            ViewData["WebhookInfo"] = info;
            return View("QBO"); // Your main view
        }

        [HttpPost]
        public async Task<IActionResult> Webhook()
        {
            // Read the request body
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();

            // Verify the webhook signature
            var signature = Request.Headers["intuit-signature"];
            if (!VerifyWebhookSignature(payload, signature, _webhookVerifierToken))
            {
                return StatusCode(401, "Invalid webhook signature");
            }

            // Process the webhook payload (e.g., customer creation event)
            // Assuming payload is JSON, deserialize it
            // Example: using System.Text.Json or Newtonsoft.Json
            // var webhookData = JsonSerializer.Deserialize<WebhookPayload>(payload);
            // Check for customer creation event and process accordingly

            // Log or process the customer creation event
            // For simplicity, log to ViewData (in production, use a queue or database)
            
            // Deserialize payload
            
            // Process customer creation events and identify company via realmId
           _webhookDataStore.StoreInfo(payload);

            // Respond with HTTP 200 within 3 seconds (QBO requirement)
            return Ok();
        }

        private bool VerifyWebhookSignature(string payload, string signature, string verifierToken)
        {
            if (string.IsNullOrEmpty(signature)) return false;

            // Compute HMAC-SHA256 of the payload using the verifier token
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(verifierToken));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var computedSignature = Convert.ToBase64String(hashBytes);

            // Compare with the provided signature
            return signature.Equals(computedSignature);
        }
        
        public async Task<IActionResult> CreateCustomer()
        {
            var apiCallFucntion = new Action<ServiceContext>(CreateNewCustomer);
            await _services.QBOApiCall(apiCallFucntion);
            return View("QBO");
        }
        public async Task<IActionResult> CreatePayment(string customerId, string invoiceId)
        {
            id = customerId;
            txnId = invoiceId;
            
            var apiCallFucntion = new Action<ServiceContext>(CreateNewPayment);
            await _services.QBOApiCall(apiCallFucntion);
            return View("QBO");
        }

        public async Task<IActionResult> VoidPayment(string paymentId)
        {
            pymtId = paymentId;
            var apiCallFucntion = new Action<ServiceContext>(CreateVoidPayment);
            await _services.QBOApiCall(apiCallFucntion);
            return View("QBO");
        }


        public async Task<IActionResult> CreateInvoice(string customerId)
        {
            id = customerId;
            var apiCallFucntion = new Action<ServiceContext>(CreateNewInvoice);
            await _services.QBOApiCall(apiCallFucntion);

            return View("QBO");
        }


        #region HelperMethods

        private void CreateNewCustomer(ServiceContext context)
        {
            var dataService = new DataService(context);
            var queryService = new QueryService<Customer>(context);
            var customer = Inputs.CreateCustomer(dataService);
            ViewData["CustomerInfo"] = "Customer with ID:" + customer.Id + " created successfully";
            ViewData["CustomerId"] = customer.Id;
        }
        private void CreateNewPayment(ServiceContext context)
        {
            var dataService = new DataService(context);
            var customerService = new QueryService<Customer>(context);
            var query = "Select * from Customer where Id='" + id + "'";
            var queryResponse = customerService.ExecuteIdsQuery(query).ToList();
            var invoice = dataService.FindById(new Invoice { Id = txnId });
            if (invoice == null || invoice.Balance < 40.00m)
            {
                throw new Exception($"Invoice ID {txnId} is invalid or has balance {invoice?.Balance ?? 0}");
            }
            // var pymtService = new QueryService<Payment>(context);
            // var payments = pymtService.ExecuteIdsQuery("Select * from Payment").ToList();
            // if (!payments.Any())
            // {
            //     Console.WriteLine("No active payment  found in the QBO company.");
            // }
            // else
            // {
            //     foreach (var pm in payments)
            //     {
            //         var jsonString = JsonSerializer.Serialize(pm);
            //         Console.WriteLine($"Payment ID: {pm.Id}, Body: {jsonString}");
            //     }
            // }

            if (queryResponse.Count >= 1)
            {
                var payment = Inputs.CreatePayment(dataService, id, txnId);
                ViewData["PaymentInfo"] = "Payment with ID:" + payment.Id + " created successfully";
                ViewData["PaymentId"] = payment.Id;
            }
            else
            {
                ViewData["PaymentInfo"] = "Invalid Customer information";
            }
            
        }

        private void CreateVoidPayment(ServiceContext context)
        {
            var dataService = new DataService(context);
            var paymentService = new QueryService<Payment>(context);
            var query = "Select * from Payment where Id='" + pymtId + "'";
            var queryResponse = paymentService.ExecuteIdsQuery(query).ToList();
            
            // var pymtService = new QueryService<Payment>(context);
            // var payments = pymtService.ExecuteIdsQuery("Select * from Payment").ToList();
            // if (!payments.Any())
            // {
            //     Console.WriteLine("No active payment  found in the QBO company.");
            // }
            // else
            // {
            //     foreach (var pm in payments)
            //     {
            //         var jsonString = JsonSerializer.Serialize(pm);
            //         Console.WriteLine($"Payment ID: {pm.Id}, Body: {jsonString}");
            //     }
            // }

            if (queryResponse.Count >= 1)
            {
                var payment = Inputs.VoidPayment(dataService, pymtId, queryResponse[0].SyncToken);
                ViewData["PaymentInfo"] = "Payment with ID:" + payment.Id + " voided successfully";
                ViewData["PaymentId"] = payment.Id;
            }
            else
            {
                ViewData["PaymentInfo"] = "Invalid Payment information";
            }

        }

        private void CreateNewInvoice(ServiceContext context)
        {
            var dataService = new DataService(context);
            var queryService = new QueryService<Account>(context);
            var customerService = new QueryService<Customer>(context);
            var query = "Select * from Customer where Id='" + id + "'";
            var queryResponse = customerService.ExecuteIdsQuery(query).ToList();
            if (queryResponse != null)
            {
                var invoice = Inputs.CreateInvoice(dataService, queryService, queryResponse[0]);
                ViewData["InvoiceInfo"] = "Invoice with ID:" + invoice.Id + " created successfully";
                ViewData["InvoiceId"] = invoice.Id;
            }
            else
            {
                ViewData["InvoiceInfo"] = "Invalid Customer information";
            }

            ViewData["CustomerId"] = id;
        }

        #endregion
    }
}