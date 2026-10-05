using System.Globalization;
using System.Text;

namespace Aiyara.Timesheet.Component.Abstractions.Emails.Templates;

/// <summary>
/// Builds the HTML email sent to a vendor when a Purchase Order is issued
/// </summary>
public static class PurchaseOrderEmailTemplate
{
    private static readonly CultureInfo ThaiCulture = CultureInfo.GetCultureInfo("th-TH");

    public record Item(string ProductCode, string ProductName, int Quantity, decimal? UnitCostQuoted);

    public static string Build(int purchaseOrderId, string vendorName, DateTime createdOn, IReadOnlyList<Item> items)
    {
        var rows = new StringBuilder();
        foreach (var item in items)
        {
            var unitCost = item.UnitCostQuoted.HasValue ? item.UnitCostQuoted.Value.ToString("C", ThaiCulture) : "-";
            rows.Append($"""
                <tr>
                  <td style="padding: 8px 12px 8px 0; border-bottom: 1px solid #eeeeee;">
                    {item.ProductName}<br />
                    <span style="color: #999999; font-size: 12px;">รหัสสินค้า: {item.ProductCode}</span>
                  </td>
                  <td style="padding: 8px 12px; border-bottom: 1px solid #eeeeee; text-align: center;">{item.Quantity}</td>
                  <td style="padding: 8px 0; border-bottom: 1px solid #eeeeee; text-align: right;">{unitCost}</td>
                </tr>
                """);
        }

        return $"""
            <html>
              <body style="font-family: Arial, sans-serif; color: #333333; line-height: 1.5;">
                <h2 style="color: #1a1a1a;">ShoppingOnline</h2>
                <p>เรียน {vendorName}</p>
                <p>ทาง ShoppingOnline ขอออกใบสั่งซื้อ (Purchase Order) ดังรายละเอียดต่อไปนี้ กรุณาจัดส่งสินค้าตามรายการด้านล่าง</p>
                <table style="border-collapse: collapse; margin: 16px 0;">
                  <tr>
                    <td style="padding: 4px 12px 4px 0; color: #666666;">เลขที่ใบสั่งซื้อ</td>
                    <td style="padding: 4px 0; font-weight: bold;">PO-{purchaseOrderId:D5}</td>
                  </tr>
                  <tr>
                    <td style="padding: 4px 12px 4px 0; color: #666666;">วันที่ออกใบสั่งซื้อ</td>
                    <td style="padding: 4px 0; font-weight: bold;">{createdOn.ToString("d MMMM yyyy", ThaiCulture)}</td>
                  </tr>
                </table>
                <table style="border-collapse: collapse; width: 100%; max-width: 480px;">
                  <thead>
                    <tr>
                      <th style="padding: 4px 12px 8px 0; text-align: left; border-bottom: 2px solid #333333;">สินค้า</th>
                      <th style="padding: 4px 12px 8px; text-align: center; border-bottom: 2px solid #333333;">จำนวนที่สั่ง</th>
                      <th style="padding: 4px 0 8px; text-align: right; border-bottom: 2px solid #333333;">ราคาต่อหน่วย (อ้างอิง)</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows}
                  </tbody>
                </table>
                <p style="color: #999999; font-size: 12px; margin-top: 24px;">อีเมลนี้ส่งโดยอัตโนมัติจากระบบ ShoppingOnline</p>
              </body>
            </html>
            """;
    }
}
