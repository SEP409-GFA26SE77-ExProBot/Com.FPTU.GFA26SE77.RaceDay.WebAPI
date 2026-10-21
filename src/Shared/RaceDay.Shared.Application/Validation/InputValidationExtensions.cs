using FluentValidation;

namespace RaceDay.Shared.Application.Validation;

public static class InputValidationExtensions
{
    public static IRuleBuilderOptions<T, string> VietnamPhone<T>(this IRuleBuilder<T, string> rule)
        => rule
            .Must(InputRules.IsValidVietnamPhone)
            .WithMessage("Số điện thoại không hợp lệ (VD: 0901234567 hoặc +84901234567).");

    public static IRuleBuilderOptions<T, string?> OptionalVietnamPhone<T>(this IRuleBuilder<T, string?> rule)
        => rule
            .Must(InputRules.IsValidOptionalVietnamPhone)
            .WithMessage("Số điện thoại không hợp lệ (VD: 0901234567 hoặc +84901234567).");

    public static IRuleBuilderOptions<T, DateOnly?> DateOfBirth<T>(this IRuleBuilder<T, DateOnly?> rule)
        => rule
            .Must(d => InputRules.IsValidDateOfBirth(d, DateOnly.FromDateTime(DateTime.UtcNow)))
            .WithMessage($"Ngày sinh không hợp lệ (tuổi từ {InputRules.MinRunnerAgeYears}–{InputRules.MaxPersonAgeYears}, không ở tương lai).");

    public static IRuleBuilderOptions<T, DateOnly> EventDate<T>(this IRuleBuilder<T, DateOnly> rule)
        => rule
            .Must(d => InputRules.IsValidEventDate(d, DateOnly.FromDateTime(DateTime.UtcNow)))
            .WithMessage($"Ngày giải phải trong khoảng {InputRules.MaxEventDaysPast} ngày trước đến {InputRules.MaxEventYearsAhead} năm tới.");

    public static IRuleBuilderOptions<T, double> Latitude<T>(this IRuleBuilder<T, double> rule)
        => rule
            .Must(InputRules.IsValidLatitude)
            .WithMessage("Vĩ độ (lat) phải trong khoảng -90…90.");

    public static IRuleBuilderOptions<T, double> Longitude<T>(this IRuleBuilder<T, double> rule)
        => rule
            .Must(InputRules.IsValidLongitude)
            .WithMessage("Kinh độ (lng) phải trong khoảng -180…180.");

    public static IRuleBuilderOptions<T, string?> OptionalIdNumber<T>(this IRuleBuilder<T, string?> rule)
        => rule
            .Must(InputRules.IsValidOptionalIdNumber)
            .WithMessage(
                "Số CCCD/CMND phải gồm 9 hoặc 12 chữ số; hộ chiếu 6–12 ký tự bắt đầu bằng chữ (VD: B1234567).");

    public static IRuleBuilderOptions<T, string?> OptionalBloodType<T>(this IRuleBuilder<T, string?> rule)
        => rule
            .Must(InputRules.IsValidOptionalBloodType)
            .WithMessage("Nhóm máu không hợp lệ (VD: O, A, B, AB hoặc O+/A-/…).");

    public static IRuleBuilderOptions<T, string?> OptionalGenderLabel<T>(this IRuleBuilder<T, string?> rule)
        => rule
            .Must(InputRules.IsValidOptionalGender)
            .WithMessage("Giới tính không hợp lệ (Male/Female/Other hoặc Nam/Nữ).");

    public static IRuleBuilderOptions<T, string?> OptionalHttpUrl<T>(this IRuleBuilder<T, string?> rule)
        => rule
            .Must(InputRules.IsValidOptionalHttpUrl)
            .WithMessage("URL không hợp lệ (http/https hoặc đường dẫn bắt đầu bằng /).");

    public static IRuleBuilderOptions<T, int?> OptionalHeightCm<T>(this IRuleBuilder<T, int?> rule)
        => rule
            .Must(h => h is null || (h >= InputRules.MinHeightCm && h <= InputRules.MaxHeightCm))
            .WithMessage($"Chiều cao phải từ {InputRules.MinHeightCm}–{InputRules.MaxHeightCm} cm.");

    public static IRuleBuilderOptions<T, int?> OptionalWeightKg<T>(this IRuleBuilder<T, int?> rule)
        => rule
            .Must(w => w is null || (w >= InputRules.MinWeightKg && w <= InputRules.MaxWeightKg))
            .WithMessage($"Cân nặng phải từ {InputRules.MinWeightKg}–{InputRules.MaxWeightKg} kg.");
}
