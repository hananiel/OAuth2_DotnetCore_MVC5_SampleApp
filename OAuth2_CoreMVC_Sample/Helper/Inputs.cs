using System;
using System.Collections.Generic;
using System.Linq;
using Intuit.Ipp.Data;
using Intuit.Ipp.DataService;
using Intuit.Ipp.QueryFilter;

namespace OAuth2_CoreMVC_Sample.Helper
{
    public class Inputs
    {
        internal static Customer CreateCustomer(DataService dataService)
        {
            var rnd = new Random();
            var newCust = new Customer
            {
                DisplayName = "Testing Company" + rnd.NextDouble(),
                GivenName = "Testing Company" + rnd.NextDouble(),
                FamilyName = "Testing Company" + rnd.NextDouble()
            };
            var response = dataService.Add(newCust);
            return response;
        }
        internal static Payment CreatePayment(DataService dataService, string customerId, string txnId)
        {
            var rnd = new Random();
            var newPymt = new Payment
            {
                // Required: Reference to the customer (ensure customerId is valid in QBO)
                CustomerRef = new ReferenceType { Value = customerId },
    
                // Required: Total payment amount, must match the sum of Line.Amount
                TotalAmt = 40.00m,
                TotalAmtSpecified = true,
    
                // Optional: Transaction date, set explicitly for consistency
                TxnDate = DateTime.Parse("2025-08-14"),
                TxnDateSpecified = true,
    
                // Optional: Payment method (e.g., Cash, Check, Credit Card; ensure ID is valid)
                PaymentMethodRef = new ReferenceType { Value = "3" }, // Adjust to valid payment method ID
    
                // Optional but recommended: Account to deposit the payment (e.g., Undeposited Funds or Bank Account)
                DepositToAccountRef = new ReferenceType { Value = "35" }, // Adjust to valid account ID
    
                // Optional: Currency (default to USD if not specified)
                CurrencyRef = new ReferenceType { Value = "USD" },
    
                // Optional: Set to false for manual payment recording
                ProcessPayment = false,
                ProcessPaymentSpecified = true,
    
                // Optional: Note for internal reference
                PrivateNote = "Payment for Invoice #" + txnId,
    
                // Required: Line items to apply the payment to an invoice
                Line = new Line[]
                {
                    new Line
                    {
                        Amount = 40.00m, // Must match TotalAmt and be <= invoice balance
                        AmountSpecified = true,
                        LinkedTxn = new LinkedTxn[]
                        {
                            new LinkedTxn
                            {
                                TxnId = txnId, // Ensure this is a valid, open invoice ID
                                TxnType = "Invoice"
                            }
                        },
                        DetailType = LineDetailTypeEnum.PaymentLineDetail, // Explicitly set line type
                        DetailTypeSpecified = true
                    }
                }
            };
            var response = dataService.Add(newPymt);
            return response;
        }
        
        // internal static Payment VoidPayment(DataService dataService, string paymentId, string syncToken)
        // {
        //     // Create a Payment object with the minimum required fields for voiding
        //     var paymentToVoid = new Payment
        //     {
        //         Id = paymentId, // The ID of the payment to void
        //         SyncToken = syncToken // The current SyncToken to ensure data consistency
        //     };
        //
        //     // Call the Void method on the DataService
        //     var response = dataService.Void(paymentToVoid);
        //
        //     return response; // Returns the voided payment object
        // }
        internal static Payment VoidPayment(DataService dataService, string paymentId, string syncToken)
        {
            var random = new Random();
            // Create a Payment object with the minimum required fields for voiding
            var paymentToVoid = new Payment
            {
                Id = paymentId, // The ID of the payment to void
                SyncToken = syncToken, // The current SyncToken to ensure data consistency
                sparse = true, // Indicate a sparse update
                sparseSpecified = true // Ensure the sparse field is included in the request
            };

            var batchItemId = "1L";
            // Create a BatchItemRequest for the void operation
            var batchItemRequest = new BatchItemRequest
            {
                bId = batchItemId, // Unique batch item ID
                operation = OperationEnum.update, // Specify update operation
                operationSpecified = true, // Ensure operation is included
                optionsData = "void", // Set optionsData to void the payment
                AnyIntuitObject = paymentToVoid, // Assign Payment to AnyIntuitObject
            };

            // Create a Batch object and add the BatchItemRequest
            var batch = dataService.CreateNewBatch();
            // Add the payment to batch with 'void' operation
            batch.Add(paymentToVoid,
                "VoidPaymentBatch"+random.NextDouble(),
                operation: OperationEnum.update,
                optionsData: ["include=void"]);

            // Execute batch
            batch.Execute();
            
            // Retrieve and check the result
           var result =  batch.IntuitBatchItemResponses[0];
            if (result.Exception != null)
            {
                throw new Exception($"Error voiding payment: {result.Exception.Message}");
            }

            // Return the voided payment from the batch result
            return result.Entity as Payment;
        }

        internal static Invoice CreateInvoice(DataService dataService, QueryService<Account> queryService,
            Customer customer)
        {
            var item = ItemCreate(dataService, queryService);
            var line = new Line
            {
                DetailType = LineDetailTypeEnum.SalesItemLineDetail,
                DetailTypeSpecified = true,
                Description = "Sample for Reimburse Charge with Invoice.",
                Amount = new decimal(40),
                AmountSpecified = true
            };
            var lineDetail = new SalesItemLineDetail
            {
                ItemRef = new ReferenceType {name = item.Name, Value = item.Id}
            };
            line.AnyIntuitObject = lineDetail;

            Line[] lines = {line};

            var invoice = new Invoice
            {
                Line = lines,
                CustomerRef = new ReferenceType {name = customer.DisplayName, Value = customer.Id},
                TxnDate = DateTime.Now.Date
            };

            var response = dataService.Add(invoice);
            return response;
        }
    

        #region Helper methods

        internal static Item ItemCreate(DataService dataService, QueryService<Account> queryService)
        {
            var random = new Random();
            var expenseAccount = QueryOrAddAccount(dataService, queryService,
                "select * from account where AccountType='Cost of Goods Sold'", AccountTypeEnum.CostofGoodsSold,
                AccountClassificationEnum.Expense, AccountSubTypeEnum.SuppliesMaterialsCogs);
            var incomeAccount = QueryOrAddAccount(dataService, queryService,
                "select * from account where AccountType='Income'", AccountTypeEnum.Income,
                AccountClassificationEnum.Revenue, AccountSubTypeEnum.SalesOfProductIncome);
            var item = new Item
            {
                Name = "Item_" + random.NextDouble(),
                ExpenseAccountRef = new ReferenceType {name = expenseAccount.Name, Value = expenseAccount.Id},
                IncomeAccountRef = new ReferenceType {name = incomeAccount.Name, Value = incomeAccount.Id},
                Type = ItemTypeEnum.NonInventory,
                TypeSpecified = true,
                UnitPrice = new decimal(100.0),
                UnitPriceSpecified = true
            };

            var apiResponse = dataService.Add(item);
            return apiResponse;
        }

        internal static Account QueryOrAddAccount(DataService dataService, QueryService<Account> queryService,
            string query, AccountTypeEnum accountType, AccountClassificationEnum classification,
            AccountSubTypeEnum subType)
        {
            var queryResponse = queryService.ExecuteIdsQuery(query).ToList();

            if (queryResponse.Count == 0)
            {
                var account = AccountCreate(dataService, accountType, classification, subType);
                return account;
            }

            return queryResponse[0];
        }

        internal static Account AccountCreate(DataService dataService, AccountTypeEnum accountType,
            AccountClassificationEnum classification, AccountSubTypeEnum subType)
        {
            var random = new Random();
            var account = new Account
            {
                Name = "Account_" + random.NextDouble(),
                AccountType = accountType,
                AccountTypeSpecified = true,
                Classification = classification,
                ClassificationSpecified = true,
                AccountSubType = subType.ToString(),
                SubAccountSpecified = true
            };
            var apiResponse = dataService.Add(account);
            return apiResponse;
        }

        #endregion
    }
}