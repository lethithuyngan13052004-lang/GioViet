using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models
{
    public enum OrderStep
    {
        None = 0,
        AskRoute = 1,     // Tỉnh đi, tỉnh đến, trạm
        AskCargo = 2,     // Hàng hóa, cân nặng, kích thước
        AskReceiver = 3,  // Người nhận (Tên, SĐT)
        AskTime = 4,      // Thời gian lấy hàng
        Confirm = 5       // Tổng hợp và xác nhận lưu
    }

    public class ChatOrderSession
    {
        public bool IsActive { get; set; } = false;
        public OrderStep CurrentStep { get; set; } = OrderStep.None;
        
        // 1. Thông tin yêu cầu chung
        public int? TripId { get; set; }
        public DateTime? PickupTimeFrom { get; set; }
        public DateTime? PickupTimeTo { get; set; }
        public string? Note { get; set; }
        public decimal? TotalPrice { get; set; }
        public List<int> TripSuggestions { get; set; } = new();
        public List<string> TripSuggestionsInfo { get; set; } = new();

        // 2. Thông tin hàng hóa (Cargodetail)
        public decimal Weight { get; set; }
        public decimal? WeightSuggest { get; set; } // Trọng lượng AI gợi ý, cần khách xác nhận
        public decimal Length { get; set; } = 10;
        public decimal Width { get; set; } = 10;
        public decimal Height { get; set; } = 10;
        public string? Description { get; set; }
        
        public int? CargoTypeId { get; set; }
        public int? CargoTypeIdSuggest { get; set; }

        // 3. Thông tin lộ trình (Shippingroute)
        public int? FromProvinceId { get; set; }
        public int? ToProvinceId { get; set; }
        public int PickupType { get; set; } = 2; // 1: Tận nơi, 2: Trạm
        public int DeliveryType { get; set; } = 2; // 1: Tận nơi, 2: Trạm
        public string? PickupAddress { get; set; }
        public string? DeliveryAddress { get; set; }
        public int? FromStationId { get; set; }
        public int? ToStationId { get; set; }
        public string? SenderPhone { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }

        // Trạng thái logic
        public bool IsConfirmed { get; set; } = false;
        public string? LastSummary { get; set; }
    }
}
