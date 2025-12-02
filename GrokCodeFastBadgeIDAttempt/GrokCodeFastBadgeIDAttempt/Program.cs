using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

class Program
{
    // Helper function to extract badge ID from property_value_map using multiple possible field names
    static string? GetBadgeId(JsonElement props)
    {
        // Try multiple possible field names for badge ID
        string[] possibleFieldNames = { "ID", "badge_id", "BADGEID", "BadgeID", "id" };
        
        foreach (var fieldName in possibleFieldNames)
        {
            if (props.TryGetProperty(fieldName, out var element))
            {
                // Handle both numeric and string value types
                if (element.ValueKind == JsonValueKind.Number)
                {
                    // Use TryGetInt64 to safely handle different numeric types
                    if (element.TryGetInt64(out var longValue))
                    {
                        return longValue.ToString();
                    }
                    // Fall back to raw text for other numeric formats
                    return element.GetRawText();
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    return element.GetString() ?? null;
                }
            }
        }
        return null;
    }

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
        Console.WriteLine("Looking for specific badge IDs: 607, 789, 613");
        Console.WriteLine($"Using multiple field names (ID, badge_id, BADGEID) and handling string/numeric types");
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

                // Step 2: Look for specific badge IDs (607, 789, 613)
                Console.WriteLine("[2] Looking for specific badge IDs: 607, 789, 613...");
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

                // Debug output: Show actual property names and values from the first badge
                var badgeItems = badgeDoc.RootElement.GetProperty("item_list").EnumerateArray();
                var firstBadge = badgeItems.FirstOrDefault();
                if (firstBadge.ValueKind != JsonValueKind.Undefined)
                {
                    Console.WriteLine("[DEBUG] Sample badge properties from API:");
                    var sampleProps = firstBadge.GetProperty("property_value_map");
                    foreach (var prop in sampleProps.EnumerateObject())
                    {
                        Console.WriteLine($"  {prop.Name} ({prop.Value.ValueKind}): {prop.Value}");
                    }
                    Console.WriteLine();
                }

                // Look for the specific badge IDs - use strings for flexible comparison
                var targetBadgeIds = new string[] { "607", "789", "613" };
                var foundBadges = new System.Collections.Generic.Dictionary<string, JsonElement>();
                badgeItems = badgeDoc.RootElement.GetProperty("item_list").EnumerateArray();

                foreach (var badge in badgeItems)
                {
                    var props = badge.GetProperty("property_value_map");
                    var badgeId = GetBadgeId(props);
                    if (badgeId != null && targetBadgeIds.Contains(badgeId))
                    {
                        foundBadges[badgeId] = badge;
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

                        // Show EMPID (employee ID) - handle both string and numeric types
                        string empId = "N/A";
                        if (props.TryGetProperty("EMPID", out var eid))
                        {
                            empId = eid.ValueKind == JsonValueKind.Number 
                                ? eid.GetInt64().ToString() 
                                : eid.GetString() ?? "N/A";
                        }
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
                Console.WriteLine("[3] Complete list of ALL badges (badge ID and EMPID):");
                Console.WriteLine();

                badgeItems = badgeDoc.RootElement.GetProperty("item_list").EnumerateArray();
                foreach (var badge in badgeItems)
                {
                    var props = badge.GetProperty("property_value_map");
                    var badgeId = GetBadgeId(props) ?? "N/A";
                    
                    // Handle EMPID as both string and numeric
                    string empId = "N/A";
                    if (props.TryGetProperty("EMPID", out var eid))
                    {
                        empId = eid.ValueKind == JsonValueKind.Number 
                            ? eid.GetInt64().ToString() 
                            : eid.GetString() ?? "N/A";
                    }

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

                    // Handle ID as both string and numeric
                    string cardholderId = "N/A";
                    if (props.TryGetProperty("ID", out var id))
                    {
                        cardholderId = id.ValueKind == JsonValueKind.Number 
                            ? id.GetInt64().ToString() 
                            : id.GetString() ?? "N/A";
                    }
                    
                    // Handle EMPID as both string and numeric
                    string empId = "N/A";
                    if (props.TryGetProperty("EMPID", out var eid))
                    {
                        empId = eid.ValueKind == JsonValueKind.Number 
                            ? eid.GetInt64().ToString() 
                            : eid.GetString() ?? "N/A";
                    }
                    
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
                        
                        // Handle badge EMPID as both string and numeric
                        string badgeEmpId = "N/A";
                        if (badgeProps.TryGetProperty("EMPID", out var beid))
                        {
                            badgeEmpId = beid.ValueKind == JsonValueKind.Number 
                                ? beid.GetInt64().ToString() 
                                : beid.GetString() ?? "N/A";
                        }

                        if (badgeEmpId == empId)
                        {
                            var badgeId = GetBadgeId(badgeProps) ?? "N/A";
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
                Console.WriteLine("• Using multiple field names (ID, badge_id, BADGEID) for badge identification");
                Console.WriteLine("• Handling both string and numeric value types");
                Console.WriteLine("• Target badge IDs: 607, 789, 613");
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
                    
                    // Handle EMPID as both string and numeric
                    string empId = "N/A";
                    if (props.TryGetProperty("EMPID", out var eid))
                    {
                        empId = eid.ValueKind == JsonValueKind.Number 
                            ? eid.GetInt64().ToString() 
                            : eid.GetString() ?? "N/A";
                    }
                    
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