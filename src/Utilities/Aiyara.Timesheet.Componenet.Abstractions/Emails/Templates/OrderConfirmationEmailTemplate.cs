using System.Globalization;
using System.Text;

namespace Aiyara.Timesheet.Component.Abstractions.Emails.Templates;

/// <summary>
/// Builds the HTML email sent to a customer confirming a newly placed order
/// </summary>
public static class OrderConfirmationEmailTemplate
{
    private static readonly CultureInfo ThaiCulture = CultureInfo.GetCultureInfo("th-TH");

    public record Item(string ProductName, int Quantity, decimal LineTotal);

    public static string Build(int orderId, DateTime orderDate, IReadOnlyList<Item> items, decimal totalAmount, string shippingAddress)
    {
        var rows = new StringBuilder();
        foreach (var item in items)
        {
            rows.Append($"""
                <tr>
                  <td style="padding: 8px 12px 8px 0; border-bottom: 1px solid #eeeeee;">{item.ProductName}</td>
                  <td style="padding: 8px 12px; border-bottom: 1px solid #eeeeee; text-align: center;">{item.Quantity}</td>
                  <td style="padding: 8px 0; border-bottom: 1px solid #eeeeee; text-align: right;">{item.LineTotal.ToString("C", ThaiCulture)}</td>
                </tr>
                """);
        }

        return $"""
            <html>
              <body style="font-family: Arial, sans-serif; color: #333333; line-height: 1.5;">
                <h2 style="color: #1a1a1a;">ShoppingOnline</h2>
                <p>ขอบคุณสำหรับการสั่งซื้อ! เราได้รับคำสั่งซื้อของคุณเรียบร้อยแล้ว (ราคารวมภาษีแล้ว)</p>
                <table style="border-collapse: collapse; margin: 16px 0;">
                  <tr>
                    <td style="padding: 4px 12px 4px 0; color: #666666;">หมายเลขคำสั่งซื้อ</td>
                    <td style="padding: 4px 0; font-weight: bold;">#{orderId}</td>
                  </tr>
                  <tr>
                    <td style="padding: 4px 12px 4px 0; color: #666666;">วันที่สั่งซื้อ</td>
                    <td style="padding: 4px 0; font-weight: bold;">{orderDate.ToString("d MMMM yyyy HH:mm", ThaiCulture)}</td>
                  </tr>
                  <tr>
                    <td style="padding: 4px 12px 4px 0; color: #666666; vertical-align: top;">จัดส่งไปที่</td>
                    <td style="padding: 4px 0; font-weight: bold;">{shippingAddress}</td>
                  </tr>
                </table>
                <table style="border-collapse: collapse; width: 100%; max-width: 480px;">
                  <thead>
                    <tr>
                      <th style="padding: 4px 12px 8px 0; text-align: left; border-bottom: 2px solid #333333;">สินค้า</th>
                      <th style="padding: 4px 12px 8px; text-align: center; border-bottom: 2px solid #333333;">จำนวน</th>
                      <th style="padding: 4px 0 8px; text-align: right; border-bottom: 2px solid #333333;">ราคา</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows}
                  </tbody>
                  <tfoot>
                    <tr>
                      <td colspan="2" style="padding: 12px 12px 0 0; text-align: right; font-weight: bold;">ยอดรวมทั้งหมด (รวมภาษี)</td>
                      <td style="padding: 12px 0 0; text-align: right; font-weight: bold;">{totalAmount.ToString("C", ThaiCulture)}</td>
                    </tr>
                  </tfoot>
                </table>
                <p style="color: #999999; font-size: 12px; margin-top: 24px;">หากคุณไม่ได้ทำรายการนี้ กรุณาติดต่อฝ่ายสนับสนุนทันที</p>
              </body>
            </html>
            """;
    }
}
