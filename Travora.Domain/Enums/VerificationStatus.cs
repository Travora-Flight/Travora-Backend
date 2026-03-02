namespace Travora.Domain.Enums;

public enum VerificationStatus
{
    Pending = 1,                // في الانتظار
    UnderReview = 2,            // تحت المراجعة
    Approved = 3,               // تمت الموافقة
    Rejected = 4,               // مرفوض
    Expired = 5,                // منتهي الصلاحية
    ResubmissionRequired = 6    // مطلوب إعادة التقديم
}
