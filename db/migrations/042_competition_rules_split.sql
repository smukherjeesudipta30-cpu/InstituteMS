SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    042_competition_rules_split.sql
    -------------------------------------------------------------------------
    Separate rules for drawing and for dance.

    The owner: "Rules will be separated for dance and drwaing".

    One combined list was never quite right, and the portal showed it. A parent
    entering only the drawing competition read "A dance performance must be less
    than 2 minutes" and had to work out that it did not apply to them; a dance
    entrant read about pens and scales for the same reason. The two printed posters
    are separate sheets, and so are the rules on them.

    Four columns now, two per competition, because the portal shows English and
    Bengali side by side.

    The old combined text is split into the two lists below, taken from the printed
    posters rather than sliced up mechanically -- some lines belong to both (the
    judges' decision, arriving early) and are repeated deliberately, because each
    list has to stand alone in front of somebody who reads only one of them.

    RUN WITH -f 65001. The file is UTF-8 and carries Bengali.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Competition') AND name = 'DrawingRulesEnglish')
BEGIN
    ALTER TABLE dbo.Competition ADD
        DrawingRulesEnglish nvarchar(max) NULL,
        DrawingRulesBengali nvarchar(max) NULL,
        DanceRulesEnglish   nvarchar(max) NULL,
        DanceRulesBengali   nvarchar(max) NULL;

    PRINT 'Added the four separated rule columns.';
END
GO

UPDATE dbo.Competition
SET DrawingRulesEnglish = N'• Competitors must reach the venue at least 30 minutes before the competition starts.' + CHAR(13) + CHAR(10) +
                          N'• Bring your own drawing materials. Paper is supplied by the institute.' + CHAR(13) + CHAR(10) +
                          N'• Pen, sketch pen and scale may not be used.' + CHAR(13) + CHAR(10) +
                          N'• All divisions begin at the same time.' + CHAR(13) + CHAR(10) +
                          N'• Register within the given time, or you cannot take part.' + CHAR(13) + CHAR(10) +
                          N'• The judges'' decision is final and binding.',

    DrawingRulesBengali = N'• প্রতিযোগীকে প্রতিযোগিতা শুরু হওয়ার অন্তত ৩০ মিনিট আগে প্রতিযোগিতা স্থানে উপস্থিত থাকতে হবে।' + CHAR(13) + CHAR(10) +
                          N'• আঁকার সরঞ্জাম নিজের সঙ্গে আনতে হবে। আঁকার কাগজ প্রতিষ্ঠান থেকে দেওয়া হবে।' + CHAR(13) + CHAR(10) +
                          N'• কোনো রকম পেন, স্কেচ পেন, স্কেল ব্যবহার করা যাবে না।' + CHAR(13) + CHAR(10) +
                          N'• সকল বিভাগের প্রতিযোগিতা একই সময়ে শুরু হবে।' + CHAR(13) + CHAR(10) +
                          N'• নির্ধারিত সময়ের মধ্যে নাম নথিভুক্ত করতে হবে, নচেৎ প্রতিযোগিতায় অংশগ্রহণ করা যাবে না।' + CHAR(13) + CHAR(10) +
                          N'• বিচারকের সিদ্ধান্তই চূড়ান্ত বলে মান্য হবে।',

    DanceRulesEnglish   = N'• The performance must be less than 2 minutes.' + CHAR(13) + CHAR(10) +
                          N'• Arrive before the scheduled time to collect your chest number.' + CHAR(13) + CHAR(10) +
                          N'• Bring the printed chest number and wear it during the performance.' + CHAR(13) + CHAR(10) +
                          N'• 40% of the registration fee is refunded in cash at the venue on the programme day.' + CHAR(13) + CHAR(10) +
                          N'• If a participant is absent on the day, the fee is not refunded.' + CHAR(13) + CHAR(10) +
                          N'• Birth certificate or age proof may be asked for.' + CHAR(13) + CHAR(10) +
                          N'• The judges'' decision is final and binding.',

    DanceRulesBengali   = N'• নৃত্যের সময়সীমা ২ মিনিটের কম হতে হবে।' + CHAR(13) + CHAR(10) +
                          N'• নির্ধারিত সময়ের আগে উপস্থিত হয়ে ব্যাচ নম্বর সংগ্রহ করতে হবে।' + CHAR(13) + CHAR(10) +
                          N'• ব্যাচ নম্বরের প্রিন্ট সঙ্গে আনতে হবে এবং নৃত্যের সময় বুকে লাগাতে হবে।' + CHAR(13) + CHAR(10) +
                          N'• অনুষ্ঠানের দিন অনুষ্ঠানস্থলেই রেজিস্ট্রেশন ফি-এর ৪০% নগদে ফেরত দেওয়া হবে।' + CHAR(13) + CHAR(10) +
                          N'• প্রতিযোগী অনুপস্থিত থাকলে কোনো টাকা ফেরত দেওয়া হবে না।' + CHAR(13) + CHAR(10) +
                          N'• প্রয়োজনে জন্ম শংসাপত্র বা বয়সের প্রমাণ চাওয়া হতে পারে।' + CHAR(13) + CHAR(10) +
                          N'• বিচারকের সিদ্ধান্তই চূড়ান্ত বলে মান্য হবে।'
WHERE DrawingRulesEnglish IS NULL;
GO

/* The combined columns go, so nothing can read a stale copy of the old list. */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Competition') AND name = 'RulesEnglish')
BEGIN
    ALTER TABLE dbo.Competition DROP COLUMN RulesEnglish, RulesBengali;
    PRINT 'Dropped the combined rule columns.';
END
GO

SELECT drawing_en = LEN(DrawingRulesEnglish), drawing_bn = LEN(DrawingRulesBengali),
       dance_en   = LEN(DanceRulesEnglish),   dance_bn   = LEN(DanceRulesBengali),
       bengali_ok = UNICODE(SUBSTRING(DrawingRulesBengali, 3, 1))
FROM   dbo.Competition;
GO
