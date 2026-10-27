namespace DocViewer.Api.Tests;

/// <summary>Source payloads in each system's own format (DESIGN.md Appendix B).</summary>
internal static class Payloads
{
    public const string Vin = "MA3EWB81S00100001";

    // Invoice BLR/24/0311 exists in both systems with the same date ⇒ POSSIBLE_DUPLICATE.
    // Insurance expired in 2020 ⇒ EXPIRED. PUC valid to 2099 ⇒ VALID.
    public const string Sales = """
        {"vehicle":{"vin":"MA3EWB81S00100001"},"documents":[
          {"docId":"INV-2024-0311","docType":"SALE_INVOICE","title":"Sales Invoice","issuedOn":"2024-02-11","validUntil":null,
           "meta":{"refNo":"BLR/24/0311","dealer":"Nexa Koramangala"}},
          {"docId":"INS-1","docType":"INSURANCE_POLICY","title":"Insurance","issuedOn":"2019-01-01","validUntil":"2020-01-01","meta":{}}
        ]}
        """;

    public const string Service = """
        {"records":[
          {"record_no":"SV-INV-BLR/24/0311","category":"INV","description":"Invoice copy","created":"11/02/2024","expiry":null,
           "workshop":"Koramangala","odometer_km":0},
          {"record_no":"SV-PUC-1","category":"PUC","description":"PUC certificate","created":"01/03/2026","expiry":"31/08/2099",
           "workshop":"Koramangala","odometer_km":12000}
        ]}
        """;

    public const string ServiceEmpty = """{"records":[]}""";

    public const string SalesNotFound = """{"error":"vehicle_not_found"}""";
}
