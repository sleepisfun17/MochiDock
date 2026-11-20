namespace MochiDock.Core;

public record HolidayItem(string Date, string NameLocal, string NameEn, string Country);

public static class HolidayEngine
{
    private static readonly List<HolidayItem> BuiltInHolidays = new()
    {
        // 2025 Japan
        new("2025-01-01", "元日", "New Year's Day", "jp"),
        new("2025-01-13", "成人の日", "Coming of Age Day", "jp"),
        new("2025-02-11", "建国記念の日", "National Foundation Day", "jp"),
        new("2025-02-23", "天皇誕生日", "Emperor's Birthday", "jp"),
        new("2025-02-24", "振替休日", "Substitute Holiday", "jp"),
        new("2025-03-20", "春分の日", "Vernal Equinox Day", "jp"),
        new("2025-04-29", "昭和の日", "Showa Day", "jp"),
        new("2025-05-03", "憲法記念日", "Constitution Memorial Day", "jp"),
        new("2025-05-04", "みどりの日", "Greenery Day", "jp"),
        new("2025-05-05", "こどもの日", "Children's Day", "jp"),
        new("2025-05-06", "振替休日", "Substitute Holiday", "jp"),
        new("2025-07-21", "海の日", "Marine Day", "jp"),
        new("2025-08-11", "山の日", "Mountain Day", "jp"),
        new("2025-09-15", "敬老の日", "Respect for the Aged Day", "jp"),
        new("2025-09-23", "秋分の日", "Autumnal Equinox Day", "jp"),
        new("2025-10-13", "スポーツの日", "Sports Day", "jp"),
        new("2025-11-03", "文化の日", "Culture Day", "jp"),
        new("2025-11-23", "勤労感謝の日", "Labor Thanksgiving Day", "jp"),
        new("2025-11-24", "振替休日", "Substitute Holiday", "jp"),

        // 2026 Japan
        new("2026-01-01", "元日", "New Year's Day", "jp"),
        new("2026-01-12", "成人の日", "Coming of Age Day", "jp"),
        new("2026-02-11", "建国記念の日", "National Foundation Day", "jp"),
        new("2026-02-23", "天皇誕生日", "Emperor's Birthday", "jp"),
        new("2026-03-20", "春分の日", "Vernal Equinox Day", "jp"),
        new("2026-04-29", "昭和の日", "Showa Day", "jp"),
        new("2026-05-03", "憲法記念日", "Constitution Memorial Day", "jp"),
        new("2026-05-04", "みどりの日", "Greenery Day", "jp"),
        new("2026-05-05", "こどもの日", "Children's Day", "jp"),
        new("2026-05-06", "振替休日", "Substitute Holiday", "jp"),
        new("2026-07-20", "海の日", "Marine Day", "jp"),
        new("2026-08-11", "山の日", "Mountain Day", "jp"),
        new("2026-09-21", "敬老の日", "Respect for the Aged Day", "jp"),
        new("2026-09-22", "国民の休日", "Citizens' Holiday", "jp"),
        new("2026-09-23", "秋分の日", "Autumnal Equinox Day", "jp"),
        new("2026-10-12", "スポーツの日", "Sports Day", "jp"),
        new("2026-11-03", "文化の日", "Culture Day", "jp"),
        new("2026-11-23", "勤労感謝の日", "Labor Thanksgiving Day", "jp"),

        // 2027 Japan
        new("2027-01-01", "元日", "New Year's Day", "jp"),
        new("2027-01-11", "成人の日", "Coming of Age Day", "jp"),
        new("2027-02-11", "建国記念の日", "National Foundation Day", "jp"),
        new("2027-02-23", "天皇誕生日", "Emperor's Birthday", "jp"),
        new("2027-03-21", "春分の日", "Vernal Equinox Day", "jp"),
        new("2027-03-22", "振替休日", "Substitute Holiday", "jp"),
        new("2027-04-29", "昭和の日", "Showa Day", "jp"),
        new("2027-05-03", "憲法記念日", "Constitution Memorial Day", "jp"),
        new("2027-05-04", "みどりの日", "Greenery Day", "jp"),
        new("2027-05-05", "こどもの日", "Children's Day", "jp"),
        new("2027-07-19", "海の日", "Marine Day", "jp"),
        new("2027-08-11", "山の日", "Mountain Day", "jp"),
        new("2027-09-20", "敬老の日", "Respect for the Aged Day", "jp"),
        new("2027-09-23", "秋分の日", "Autumnal Equinox Day", "jp"),
        new("2027-10-11", "スポーツの日", "Sports Day", "jp"),
        new("2027-11-03", "文化の日", "Culture Day", "jp"),
        new("2027-11-23", "勤労感謝の日", "Labor Thanksgiving Day", "jp"),

        // 2025 Indonesia
        new("2025-01-01", "Tahun Baru Masehi", "New Year's Day", "id"),
        new("2025-01-27", "Isra Mi'raj", "Ascension of the Prophet", "id"),
        new("2025-01-29", "Tahun Baru Imlek", "Chinese New Year", "id"),
        new("2025-03-29", "Hari Suci Nyepi", "Nyepi", "id"),
        new("2025-03-31", "Idul Fitri 1446 H (Hari 1)", "Eid al-Fitr Day 1", "id"),
        new("2025-04-01", "Idul Fitri 1446 H (Hari 2)", "Eid al-Fitr Day 2", "id"),
        new("2025-04-18", "Wafat Yesus Kristus", "Good Friday", "id"),
        new("2025-04-20", "Hari Paskah", "Easter Sunday", "id"),
        new("2025-05-01", "Hari Buruh Internasional", "Labor Day", "id"),
        new("2025-05-12", "Hari Raya Waisak", "Vesak Day", "id"),
        new("2025-05-29", "Kenaikan Yesus Kristus", "Ascension of Jesus", "id"),
        new("2025-06-01", "Hari Lahir Pancasila", "Pancasila Day", "id"),
        new("2025-06-07", "Hari Raya Idul Adha", "Eid al-Adha", "id"),
        new("2025-06-27", "Tahun Baru Islam 1447 H", "Islamic New Year", "id"),
        new("2025-08-17", "Hari Kemerdekaan RI", "Independence Day", "id"),
        new("2025-09-05", "Maulid Nabi Muhammad SAW", "Prophet's Birthday", "id"),
        new("2025-12-25", "Hari Raya Natal", "Christmas Day", "id"),

        // 2026 Indonesia
        new("2026-01-01", "Tahun Baru Masehi", "New Year's Day", "id"),
        new("2026-01-16", "Isra Mi'raj", "Ascension of the Prophet", "id"),
        new("2026-02-17", "Tahun Baru Imlek", "Chinese New Year", "id"),
        new("2026-03-19", "Cuti Bersama Idul Fitri", "Eid al-Fitr Joint Leave", "id"),
        new("2026-03-20", "Idul Fitri / Nyepi", "Eid al-Fitr / Nyepi", "id"),
        new("2026-03-21", "Idul Fitri 1447 H (Hari 2)", "Eid al-Fitr Day 2", "id"),
        new("2026-03-23", "Cuti Bersama Idul Fitri", "Eid al-Fitr Joint Leave", "id"),
        new("2026-04-03", "Wafat Yesus Kristus", "Good Friday", "id"),
        new("2026-04-05", "Hari Paskah", "Easter Sunday", "id"),
        new("2026-05-01", "Hari Buruh Internasional", "Labor Day", "id"),
        new("2026-05-14", "Kenaikan Yesus Kristus", "Ascension of Jesus", "id"),
        new("2026-05-27", "Hari Raya Idul Adha", "Eid al-Adha", "id"),
        new("2026-05-31", "Hari Raya Waisak", "Vesak Day", "id"),
        new("2026-06-01", "Hari Lahir Pancasila", "Pancasila Day", "id"),
        new("2026-06-16", "Tahun Baru Islam 1448 H", "Islamic New Year", "id"),
        new("2026-08-17", "Hari Kemerdekaan RI", "Independence Day", "id"),
        new("2026-08-25", "Maulid Nabi Muhammad SAW", "Prophet's Birthday", "id"),
        new("2026-12-25", "Hari Raya Natal", "Christmas Day", "id"),

        // 2027 Indonesia
        new("2027-01-01", "Tahun Baru Masehi", "New Year's Day", "id"),
        new("2027-01-05", "Isra Mi'raj", "Ascension of the Prophet", "id"),
        new("2027-02-06", "Tahun Baru Imlek", "Chinese New Year", "id"),
        new("2027-03-09", "Hari Suci Nyepi", "Nyepi", "id"),
        new("2027-03-10", "Hari Raya Idul Fitri", "Eid al-Fitr", "id"),
        new("2027-03-26", "Wafat Yesus Kristus", "Good Friday", "id"),
        new("2027-03-28", "Hari Paskah", "Easter Sunday", "id"),
        new("2027-05-01", "Hari Buruh Internasional", "Labor Day", "id"),
        new("2027-05-06", "Kenaikan Yesus Kristus", "Ascension of Jesus", "id"),
        new("2027-05-16", "Hari Raya Idul Adha", "Eid al-Adha", "id"),
        new("2027-05-20", "Hari Raya Waisak", "Vesak Day", "id"),
        new("2027-06-01", "Hari Lahir Pancasila", "Pancasila Day", "id"),
        new("2027-06-06", "Tahun Baru Islam 1449 H", "Islamic New Year", "id"),
        new("2027-08-15", "Maulid Nabi Muhammad SAW", "Prophet's Birthday", "id"),
        new("2027-08-17", "Hari Kemerdekaan RI", "Independence Day", "id"),
        new("2027-12-25", "Hari Raya Natal", "Christmas Day", "id")
    };

    public static IReadOnlyList<HolidayItem> GetHolidays(DateTime date, bool enableJapan, bool enableIndonesia)
    {
        string dateStr = date.ToString("yyyy-MM-dd");
        return BuiltInHolidays
            .Where(h => h.Date == dateStr &&
                        ((enableJapan && h.Country == "jp") ||
                         (enableIndonesia && h.Country == "id")))
            .ToList();
    }

    public static bool IsHoliday(DateTime date, bool enableJapan, bool enableIndonesia)
    {
        string dateStr = date.ToString("yyyy-MM-dd");
        return BuiltInHolidays.Any(h => h.Date == dateStr &&
                                        ((enableJapan && h.Country == "jp") ||
                                         (enableIndonesia && h.Country == "id")));
    }
}

