using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// PreSchool exam marks: the exams, and one sheet per exam and class to enter
/// every child's marks in every subject. Grades beside the marks come from the
/// PreSchool's grade table. Reads and writes PreSchool* tables only.
/// </summary>
public class PreSchoolMarkService : IPreSchoolMarkService
{
    private readonly AppDbContext _db;

    public PreSchoolMarkService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PreSchoolExamDto>> ExamsAsync(CancellationToken ct = default) =>
        await _db.PreSchoolExams.AsNoTracking()
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Name)
            .Select(e => new PreSchoolExamDto(e.Id, e.Name, e.HeldOn, e.SortOrder, e.IsActive))
            .ToListAsync(ct);

    public async Task<Result<PreSchoolExamDto>> SaveExamAsync(int? id, PreSchoolExamSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (await _db.PreSchoolExams.AnyAsync(e => e.Name == name && e.Id != id, ct))
            return Result<PreSchoolExamDto>.Failure($"There is already an exam called {name}.", ResultStatus.Conflict);

        PreSchoolExam? exam;
        if (id is null)
        {
            exam = new PreSchoolExam { CreatedBy = actor, CreatedOn = DateTime.Now };
            _db.PreSchoolExams.Add(exam);
        }
        else
        {
            exam = await _db.PreSchoolExams.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (exam is null) return Result<PreSchoolExamDto>.Failure("That exam no longer exists.", ResultStatus.NotFound);
            exam.ModifiedBy = actor;
            exam.ModifiedOn = DateTime.Now;
        }

        exam.Name = name;
        exam.HeldOn = request.HeldOn;
        exam.SortOrder = request.SortOrder;
        exam.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolExamDto>.Success(new PreSchoolExamDto(exam.Id, exam.Name, exam.HeldOn, exam.SortOrder, exam.IsActive));
    }

    public async Task<Result<PreSchoolMarkSheetDto>> SheetAsync(int examId, int classId, CancellationToken ct = default)
    {
        var exam = await _db.PreSchoolExams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null) return Result<PreSchoolMarkSheetDto>.Failure("That exam no longer exists.", ResultStatus.NotFound);
        var cls = await _db.PreSchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct);
        if (cls is null) return Result<PreSchoolMarkSheetDto>.Failure("That class no longer exists.", ResultStatus.NotFound);

        var subjects = await _db.PreSchoolSubjects.AsNoTracking()
            .Where(s => s.ClassId == classId && s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new PreSchoolMarkSubjectDto(s.Id, s.Name, s.MaxMarks))
            .ToListAsync(ct);

        // Children studying in the class now, plus anyone already marked in this exam.
        var marked = _db.PreSchoolMarks.Where(m => m.ExamId == examId).Select(m => m.StudentId);
        var students = await _db.PreSchoolStudents.AsNoTracking()
            .Where(s => s.ClassId == classId && (s.Status == PreSchoolStudentStatus.Active || marked.Contains(s.Id)))
            .OrderBy(s => s.ChildName)
            .Select(s => new { s.Id, s.AdmissionNo, s.ChildName })
            .ToListAsync(ct);

        var ids = students.Select(s => s.Id).ToList();
        var marks = await _db.PreSchoolMarks.AsNoTracking()
            .Where(m => m.ExamId == examId && ids.Contains(m.StudentId))
            .ToListAsync(ct);

        var bands = await _db.PreSchoolGradeBands.AsNoTracking().OrderByDescending(b => b.MinPercent).ToListAsync(ct);
        var prefix = (await _db.PreSchoolSettings.AsNoTracking().Select(x => x.ReceiptPrefix).FirstOrDefaultAsync(ct)) ?? "APS";

        var rows = students.Select(s =>
        {
            var cells = subjects.Select(sub =>
            {
                var m = marks.FirstOrDefault(x => x.StudentId == s.Id && x.SubjectId == sub.Id);
                return new PreSchoolMarkCellDto(sub.Id, m?.Marks, m?.IsAbsent ?? false);
            }).ToList();

            // Totals over what has been entered: a subject not entered yet is left out, an absence counts as zero.
            decimal total = 0, outOf = 0;
            foreach (var c in cells)
            {
                var max = subjects.First(x => x.Id == c.SubjectId).MaxMarks;
                if (c.Marks is decimal v) { total += v; outOf += max; }
                else if (c.IsAbsent) outOf += max;
            }

            decimal? percent = outOf > 0 ? Math.Round(total * 100m / outOf, 1) : null;
            var grade = percent is decimal p ? bands.FirstOrDefault(b => p >= b.MinPercent)?.Grade : null;
            return new PreSchoolMarkRowDto(s.Id, $"{prefix}-{s.AdmissionNo:0000}", s.ChildName, cells, total, outOf, percent, grade);
        }).ToList();

        return Result<PreSchoolMarkSheetDto>.Success(new PreSchoolMarkSheetDto(exam.Id, exam.Name, cls.Id, cls.Name, subjects, rows));
    }

    public async Task<Result<PreSchoolMarkSheetDto>> SaveAsync(PreSchoolMarkSaveRequest request, string? actor, CancellationToken ct = default)
    {
        if (!await _db.PreSchoolExams.AnyAsync(e => e.Id == request.ExamId, ct))
            return Result<PreSchoolMarkSheetDto>.Failure("That exam no longer exists.", ResultStatus.NotFound);

        var subjects = await _db.PreSchoolSubjects.AsNoTracking()
            .Where(s => s.ClassId == request.ClassId)
            .ToDictionaryAsync(s => s.Id, ct);
        var studentIds = (await _db.PreSchoolStudents.AsNoTracking()
            .Where(s => s.ClassId == request.ClassId)
            .Select(s => new { s.Id, s.ChildName })
            .ToListAsync(ct)).ToDictionary(s => s.Id, s => s.ChildName);

        foreach (var e in request.Entries)
        {
            if (!studentIds.ContainsKey(e.StudentId))
                return Result<PreSchoolMarkSheetDto>.Failure("A child on the sheet is not in this class. Reload and try again.");
            if (!subjects.TryGetValue(e.SubjectId, out var sub))
                return Result<PreSchoolMarkSheetDto>.Failure("A subject on the sheet is not taught in this class. Reload and try again.");
            if (e.Marks is decimal v && v > sub.MaxMarks)
                return Result<PreSchoolMarkSheetDto>.Failure(
                    $"{studentIds[e.StudentId]}: {sub.Name} is out of {sub.MaxMarks}, so {v:0.##} is too high.");
        }

        var existing = await _db.PreSchoolMarks
            .Where(m => m.ExamId == request.ExamId && studentIds.Keys.Contains(m.StudentId))
            .ToListAsync(ct);
        var now = DateTime.Now;

        foreach (var e in request.Entries)
        {
            var row = existing.FirstOrDefault(m => m.StudentId == e.StudentId && m.SubjectId == e.SubjectId);
            var empty = e.Marks is null && !e.IsAbsent;
            if (row is null)
            {
                if (empty) continue;
                _db.PreSchoolMarks.Add(new PreSchoolMark
                {
                    ExamId = request.ExamId, StudentId = e.StudentId, SubjectId = e.SubjectId,
                    Marks = e.IsAbsent ? null : e.Marks, IsAbsent = e.IsAbsent, UpdatedBy = actor, UpdatedOn = now
                });
            }
            else if (empty)
            {
                _db.PreSchoolMarks.Remove(row);
            }
            else if (row.Marks != (e.IsAbsent ? null : e.Marks) || row.IsAbsent != e.IsAbsent)
            {
                row.Marks = e.IsAbsent ? null : e.Marks;
                row.IsAbsent = e.IsAbsent;
                row.UpdatedBy = actor;
                row.UpdatedOn = now;
            }
        }

        await _db.SaveChangesAsync(ct);
        return await SheetAsync(request.ExamId, request.ClassId, ct);
    }
}
