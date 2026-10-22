-- Expense amounts must reject extra fractional digits.
-- numeric(12,2) rounds 1.235 to 1.24 before ck_expenses_amount can see it.
-- This matches 005_numeric_scale.sql. Do not edit 009_expenses.sql.

alter table expenses
    alter column amount type numeric;
