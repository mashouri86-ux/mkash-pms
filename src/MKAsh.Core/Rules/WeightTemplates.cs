using System.Text.RegularExpressions;
using MKAsh.Core.Domain;

namespace MKAsh.Core.Rules;

/// <summary>Default phase weights per project type (each row totals 100). The PMO can edit them.</summary>
public static class WeightTemplates
{
    public static readonly IReadOnlyDictionary<ProjectType, IReadOnlyDictionary<WeightPhase, int>> Defaults =
        new Dictionary<ProjectType, IReadOnlyDictionary<WeightPhase, int>>
        {
            [ProjectType.Study] = Row(10, 15, 50, 15, 10),
            [ProjectType.Policy] = Row(10, 10, 35, 35, 10),
            [ProjectType.Build] = Row(10, 20, 45, 15, 10),
            [ProjectType.Program] = Row(10, 15, 50, 15, 10),
        };

    private static IReadOnlyDictionary<WeightPhase, int> Row(int init, int proc, int exec, int launch, int close) =>
        new Dictionary<WeightPhase, int>
        {
            [WeightPhase.Init] = init,
            [WeightPhase.Proc] = proc,
            [WeightPhase.Exec] = exec,
            [WeightPhase.Launch] = launch,
            [WeightPhase.Close] = close,
        };

    private static readonly Regex Policy = new("سياس|قانون|تشريع|لائح|ترخيص|تراخيص|معايير|حوكمة", RegexOptions.Compiled);
    private static readonly Regex ProgramRx = new("برنامج|برامج|حملة|حملات|جائزة|فرصة", RegexOptions.Compiled);
    private static readonly Regex StudyRx = new("دراسة|مؤشر|مرصد|دليل|استراتيجية|بيانات|تصنيف", RegexOptions.Compiled);
    private static readonly Regex BuildRx = new("مركز|منصة|نادي|حاضن|مرفق|مرافق|خدمات", RegexOptions.Compiled);

    /// <summary>Detect the project type from its Arabic name. The PMO can override it.</summary>
    public static ProjectType DetectType(string name)
    {
        name ??= "";
        if (Policy.IsMatch(name)) return ProjectType.Policy;
        if (ProgramRx.IsMatch(name)) return ProjectType.Program;
        if (StudyRx.IsMatch(name)) return ProjectType.Study;
        if (BuildRx.IsMatch(name)) return ProjectType.Build;
        return ProjectType.Program;
    }

    /// <summary>A template row is valid only when it totals exactly 100.</summary>
    public static bool IsValid(IReadOnlyDictionary<WeightPhase, int> row) => row.Values.Sum() == 100 && row.Values.All(v => v >= 0);
}

/// <summary>Arabic keywords that tag a work package with its phase.</summary>
public static class PhaseKeywords
{
    public static readonly IReadOnlyDictionary<WeightPhase, string[]> Keywords = new Dictionary<WeightPhase, string[]>
    {
        [WeightPhase.Init] = new[] { "خطة المشروع", "تشكيل فريق", "فريق العمل", "فريق المشروع", "ميثاق", "نطاق العمل", "الخطة التفصيلية", "خطة العمل التفصيلية", "الإطار العام", "الاطار العام", "الإطار المنهجي", "إطار منهجي", "الاطار نظري", "إعداد فريق", "اعداد فريق" },
        [WeightPhase.Proc] = new[] { "مناقصة", "المناقصة", "تعاقد", "التعاقد", "جهة بحثية", "الجهة البحثية", "الجهة المنفذة", "جهة استشارية", "مورد", "كراسة", "الشراكة", "شراكات", "اتفاقيات" },
        [WeightPhase.Close] = new[] { "متابعة تبني", "التقييم", "تقييم", "التقرير النهائي", "تقرير نهائي", "الدروس المستفادة", "قياس الأثر", "الاستدامة", "الرصد والتقييم" },
        [WeightPhase.Launch] = new[] { "اعتماد", "إعتماد", "أعتماد", "تدشين", "اطلاق", "إطلاق", "الإطلاق", "اصدار", "إصدار", "افتتاح", "رفع", "الإعلان", "الاعلان", "الزامية", "إلزامية", "تسليم", "انطلاق", "وثيقه مشروع", "وثيقة مشروع", "التشغيل الكامل" },
    };

    private static readonly WeightPhase[] Order = { WeightPhase.Init, WeightPhase.Proc, WeightPhase.Close, WeightPhase.Launch };

    /// <summary>First matching phase in the order Initiation, Procurement, Closure, Launch; otherwise Execution.</summary>
    public static WeightPhase AutoPhase(string? name)
    {
        name ??= "";
        foreach (var ph in Order)
            if (Keywords[ph].Any(k => name.Contains(k, StringComparison.Ordinal))) return ph;
        return WeightPhase.Exec;
    }
}
