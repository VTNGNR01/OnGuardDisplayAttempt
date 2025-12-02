using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

class Program
{
    static async Task Main()
    {
        // Improved SSL certificate validation that accepts the server certificate
        ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
        {
            // For development/local servers, accept certificates even with minor issues
            if (sender is HttpWebRequest request)
            {
                var host = request.Address.Host.ToLower();
                if (host == "og8dev-ba" || host == "127.0.0.1" || host == "localhost")
                {
                    return true; // Accept certificate for known development hosts
                }
            }

            // For production hosts, require valid certificates
            return sslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
        };

        // Use the server hostname that matches the certificate
        var baseUrl = "https://OG8DEV-BA:8080/api/access/onguard/openaccess";
        var applicationId = "19e4f917-f7a8-4e06-9a18-b93a9a5c9755";

        Console.WriteLine("=== OnGuard Badge ID Analysis (Updated) ===");
        Console.WriteLine("Looking for specific badge IDs: 613, 789, 607, etc.");
        Console.WriteLine($"Using badge_id field and EMPID for employee identification");
        Console.WriteLine($"Connecting to: {baseUrl}");
        Console.WriteLine();

        try
        {
            using (var client = new HttpClient())
            {
                // Set timeout for slow responses
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("Application-Id", applicationId);

                // Step 1: Authenticate
                Console.WriteLine("[1] Authenticating with OnGuard...");
                var authRequest = new { user_name = "SA", password = "Password1." };
                var authContent = new StringContent(
                    JsonSerializer.Serialize(authRequest),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var authResponse = await client.PostAsync($"{baseUrl}/authentication?version=1.0", authContent);

                if (!authResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Authentication failed: {authResponse.StatusCode}");
                    var error = await authResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error: {error}");
                    return;
                }

                var authJson = await authResponse.Content.ReadAsStringAsync();
                var authDoc = JsonDocument.Parse(authJson);
                var sessionToken = authDoc.RootElement.GetProperty("session_token").GetString();

                Console.WriteLine("✓ Authentication successful");
                Console.WriteLine();

                client.DefaultRequestHeaders.Add("Session-Token", sessionToken);

                // Step 2: Look for specific badge IDs (613, 789, 607, etc.)
                Console.WriteLine("[2] Looking for specific badge IDs: 613, 789, 607...");
                var badgeUrl = $"{baseUrl}/instances?type_name=Lnl_Badge&page_size=100&version=1.0";
                var badgeResponse = await client.GetAsync(badgeUrl);

                if (!badgeResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Badge query failed: {badgeResponse.StatusCode}");
                    var error = await badgeResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error: {error}");
                    return;
                }

                var badgeJson = await badgeResponse.Content.ReadAsStringAsync();
                var badgeDoc = JsonDocument.Parse(badgeJson);
                var totalBadges = badgeDoc.RootElement.GetProperty("total_items").GetInt32();

                Console.WriteLine($"✓ Found {totalBadges} badges total");
                Console.WriteLine();

                // Look for the specific badge IDs
                var targetBadgeIds = new long[] { 613, 789, 607 };
                var foundBadges = new System.Collections.Generic.Dictionary<long, JsonElement>();
                var badgeItems = badgeDoc.RootElement.GetProperty("item_list").EnumerateArray();

                foreach (var badge in badgeItems)
                {
                    var props = badge.GetProperty("property_value_map");
                    if (props.TryGetProperty("badge_id", out var badgeIdElement))
                    {
                        var badgeId = badgeIdElement.GetInt64();
                        if (targetBadgeIds.Contains(badgeId))
                        {
                            foundBadges[badgeId] = badge;
                        }
                    }
                }

                Console.WriteLine("🎯 SEARCH RESULTS FOR TARGET BADGE IDs:");
                foreach (var targetId in targetBadgeIds)
                {
                    if (foundBadges.ContainsKey(targetId))
                    {
                        Console.WriteLine($"✅ Badge ID {targetId}: FOUND");
                        var badge = foundBadges[targetId];
                        var props = badge.GetProperty("property_value_map");

                        // Show EMPID (employee ID)
                        var empId = props.TryGetProperty("EMPID", out var eid) ? eid.GetInt64().ToString() : "N/A";
                        Console.WriteLine($"   Employee ID (EMPID): {empId}");

                        // Show all other properties
                        Console.WriteLine("   All properties:");
                        foreach (var prop in props.EnumerateObject())
                        {
                            if (prop.Name != "EMPID") // Already shown above
                            {
                                Console.WriteLine($"     {prop.Name}: {prop.Value}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ Badge ID {targetId}: NOT FOUND");
                    }
                    Console.WriteLine();
                }

                // Step 3: Show all badges with their badge_id and EMPID
                Console.WriteLine("[3] Complete list of ALL badges (badge_id and EMPID):");
                Console.WriteLine();

                badgeItems = badgeDoc.RootElement.GetProperty("item_list").EnumerateArray();
                foreach (var badge in badgeItems)
                {
                    var props = badge.GetProperty("property_value_map");
                    var badgeId = props.TryGetProperty("badge_id", out var bid) ? bid.GetInt64() : 0;
                    var empId = props.TryGetProperty("EMPID", out var eid) ? eid.GetInt64() : 0;

                    Console.WriteLine($"Badge ID {badgeId} -> Employee ID {empId}");
                }
                Console.WriteLine();

                // Step 4: Find cardholder with TESTFIELD123 and check their badge associations
                Console.WriteLine("[4] Finding TESTFIELD123 cardholder and their badge associations...");
                var cardholderUrl = $"{baseUrl}/instances?type_name=Lnl_Cardholder&page_size=100&version=1.0";
                var cardholderResponse = await client.GetAsync(cardholderUrl);

                if (!cardholderResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Cardholder query failed: {cardholderResponse.StatusCode}");
                    var error = await cardholderResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error: {error}");
                    return;
                }

                var cardholderJson = await cardholderResponse.Content.ReadAsStringAsync();
                var cardholderDoc = JsonDocument.Parse(cardholderJson);
                var cardholders = cardholderDoc.RootElement.GetProperty("item_list").EnumerateArray();

                JsonElement? testFieldCardholder = null;
                foreach (var cardholder in cardholders)
                {
                    var props = cardholder.GetProperty("property_value_map");
                    if (props.TryGetProperty("TESTFIELD123", out var testField) &&
                        !string.IsNullOrWhiteSpace(testField.GetString()))
                    {
                        testFieldCardholder = cardholder;
                        break;
                    }
                }

                if (testFieldCardholder.HasValue)
                {
                    var props = testFieldCardholder.Value.GetProperty("property_value_map");

                    var cardholderId = props.TryGetProperty("ID", out var id) ? id.GetInt64() : 0;
                    var empId = props.TryGetProperty("EMPID", out var eid) ? eid.GetInt64() : 0;
                    var firstName = props.TryGetProperty("FIRSTNAME", out var fn) ? fn.GetString() : "";
                    var lastName = props.TryGetProperty("LASTNAME", out var ln) ? ln.GetString() : "";
                    var fullName = $"{firstName} {lastName}".Trim();

                    Console.WriteLine("🎯 TESTFIELD123 CARDHOLDER FOUND:");
                    Console.WriteLine($"Name: {fullName}");
                    Console.WriteLine($"Cardholder ID: {cardholderId}");
                    Console.WriteLine($"Employee ID (EMPID): {empId}");
                    Console.WriteLine($"TESTFIELD123: '{props.GetProperty("TESTFIELD123").GetString()}'");
                    Console.WriteLine();

                    // Find badges associated with this cardholder's EMPID
                    Console.WriteLine("🔍 BADGES ASSOCIATED WITH THIS CARDHOLDER:");
                    badgeItems = badgeDoc.RootElement.GetProperty("item_list").EnumerateArray();
                    int badgeCount = 0;

                    foreach (var badge in badgeItems)
                    {
                        var badgeProps = badge.GetProperty("property_value_map");
                        var badgeEmpId = badgeProps.TryGetProperty("EMPID", out var beid) ? beid.GetInt64() : 0;

                        if (badgeEmpId == empId)
                        {
                            var badgeId = badgeProps.TryGetProperty("badge_id", out var bid) ? bid.GetInt64() : 0;
                            badgeCount++;
                            Console.WriteLine($"  • Badge ID {badgeId}");

                            // Check if this matches our target badges
                            if (targetBadgeIds.Contains(badgeId))
                            {
                                Console.WriteLine($"    🎯 This is one of your target badges!");
                            }
                        }
                    }

                    if (badgeCount == 0)
                    {
                        Console.WriteLine("  No badges found for this employee ID");
                    }
                    else
                    {
                        Console.WriteLine($"  Total badges: {badgeCount}");
                    }
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine("❌ No cardholder with TESTFIELD123 found");
                }

                // Step 5: Summary
                Console.WriteLine("=== SUMMARY ===");
                Console.WriteLine("• Using 'badge_id' field for badge identification");
                Console.WriteLine("• Using 'EMPID' field for employee identification");
                Console.WriteLine("• Target badge IDs: 613, 789, 607");
                Console.WriteLine();

                var foundCount = foundBadges.Count;
                var notFoundCount = targetBadgeIds.Length - foundCount;

                Console.WriteLine($"• Found: {foundCount} of {targetBadgeIds.Length} target badges");
                if (notFoundCount > 0)
                {
                    var notFound = targetBadgeIds.Where(id => !foundBadges.ContainsKey(id));
                    Console.WriteLine($"• Not found: {string.Join(", ", notFound)}");
                }

                if (testFieldCardholder.HasValue)
                {
                    var props = testFieldCardholder.Value.GetProperty("property_value_map");
                    var empId = props.TryGetProperty("EMPID", out var eid) ? eid.GetInt64() : 0;
                    Console.WriteLine($"• TESTFIELD123 cardholder EMPID: {empId}");
                    Console.WriteLine("• Update your application: ID = {empId} (or appropriate field)");
                    Console.WriteLine("• Update field name: TestField123 → TESTFIELD123");
                }

                Console.WriteLine();
                Console.WriteLine("=== ANALYSIS COMPLETE ===");
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine("❌ Network/Connection Error:");
            Console.WriteLine($"   {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Unexpected Error: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}