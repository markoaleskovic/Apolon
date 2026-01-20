-- 00_reset_and_create.sql (VARCHAR version for checkup_type)
-- Drops everything in schema public, then recreates ORM demo schema.
-- Compatible with your current ORM parameter handling (no Npgsql enum mapping needed).

BEGIN;

DO $$
DECLARE
  r RECORD;
BEGIN
  -- Drop tables
  FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public') LOOP
    EXECUTE 'DROP TABLE IF EXISTS public.' || quote_ident(r.tablename) || ' CASCADE;';
  END LOOP;

  -- Drop sequences
  FOR r IN (SELECT sequencename FROM pg_sequences WHERE schemaname = 'public') LOOP
    EXECUTE 'DROP SEQUENCE IF EXISTS public.' || quote_ident(r.sequencename) || ' CASCADE;';
  END LOOP;

  -- Drop enum types (and other user-defined enum types) if any exist
  FOR r IN (
    SELECT t.typname
    FROM pg_type t
    JOIN pg_namespace n ON n.oid = t.typnamespace
    WHERE n.nspname = 'public'
      AND t.typtype = 'e'
  ) LOOP
    EXECUTE 'DROP TYPE IF EXISTS public.' || quote_ident(r.typname) || ' CASCADE;';
  END LOOP;
END $$;

-- ----- TABLES -----

-- Patients (CRUD required) [file:12]
CREATE TABLE public.patients (
  id              BIGSERIAL PRIMARY KEY,
  first_name      VARCHAR(100) NOT NULL,
  last_name       VARCHAR(100) NOT NULL,
  oib             VARCHAR(32) NOT NULL UNIQUE,
  created_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Medical record (1-1 with patient)
CREATE TABLE public.medical_records (
  id              BIGSERIAL PRIMARY KEY,
  patient_id      BIGINT NOT NULL UNIQUE,
  notes           VARCHAR(2000) NOT NULL DEFAULT '',
  created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
  CONSTRAINT fk_medical_records_patient
    FOREIGN KEY (patient_id) REFERENCES public.patients(id) ON DELETE CASCADE
);

-- Checkups (CRUD required) [file:12]
-- 1-many patient->checkups
-- checkup_type stored as VARCHAR so Npgsql can send it without enum mapping.
CREATE TABLE public.checkups (
  id              BIGSERIAL PRIMARY KEY,
  patient_id      BIGINT NOT NULL,
  checkup_type    VARCHAR(16) NOT NULL,
  performed_at    TIMESTAMP NOT NULL DEFAULT NOW(),
  price           DECIMAL(10,2) NOT NULL DEFAULT 0,
  body_temp_c     FLOAT NULL,
  CONSTRAINT fk_checkups_patient
    FOREIGN KEY (patient_id) REFERENCES public.patients(id) ON DELETE CASCADE,
  CONSTRAINT ck_checkups_type
    CHECK (checkup_type IN ('GP','BLOOD','X-RAY','CT','MRI','ULTRA','EKG','ECHO','EYE','DERM','DENTA','MAMMO','EEG'))
);

-- Medications (CRUD required) [file:12]
CREATE TABLE public.medications (
  id              BIGSERIAL PRIMARY KEY,
  name            VARCHAR(200) NOT NULL UNIQUE,
  atc_code        VARCHAR(32) NULL,
  default_dosage  VARCHAR(100) NOT NULL DEFAULT '',
  created_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Prescriptions: join with payload (dosage + start/end)
-- Many-to-many checkups<->medications (with extra fields)
-- start_date/end_date use TIMESTAMP to map cleanly to C# DateTime in your ORM.
CREATE TABLE public.prescriptions (
  id              BIGSERIAL PRIMARY KEY,
  checkup_id      BIGINT NOT NULL,
  medication_id   BIGINT NOT NULL,
  dosage          VARCHAR(100) NOT NULL,
  start_date      TIMESTAMP NOT NULL,
  end_date        TIMESTAMP NULL,
  CONSTRAINT fk_prescriptions_checkup
    FOREIGN KEY (checkup_id) REFERENCES public.checkups(id) ON DELETE CASCADE,
  CONSTRAINT fk_prescriptions_medication
    FOREIGN KEY (medication_id) REFERENCES public.medications(id) ON DELETE RESTRICT
);

-- Prevent duplicate medication on same checkup
CREATE UNIQUE INDEX ux_prescriptions_checkup_med
  ON public.prescriptions(checkup_id, medication_id);

-- Helpful indexes for WHERE + join lookups
CREATE INDEX ix_checkups_patient_id ON public.checkups(patient_id);
CREATE INDEX ix_prescriptions_checkup_id ON public.prescriptions(checkup_id);
CREATE INDEX ix_prescriptions_medication_id ON public.prescriptions(medication_id);

-- ----- OPTIONAL SEED DATA -----
INSERT INTO public.patients(first_name, last_name, oib)
VALUES
('Ana','Horvat','OIB-0001'),
('Marko','Kovač','OIB-0002');

INSERT INTO public.medical_records(patient_id, notes)
SELECT id, 'Initial record' FROM public.patients;

INSERT INTO public.medications(name, atc_code, default_dosage)
VALUES
('Ibuprofen','M01AE01','200mg'),
('Amoxicillin','J01CA04','500mg');

INSERT INTO public.checkups(patient_id, checkup_type, performed_at, price, body_temp_c)
SELECT id, 'GP', NOW(), 20.00, 36.6
FROM public.patients;

-- One example prescription (first checkup + first medication)
INSERT INTO public.prescriptions(checkup_id, medication_id, dosage, start_date, end_date)
SELECT c.id, m.id, '1x daily', NOW(), NULL
FROM public.checkups c
CROSS JOIN LATERAL (SELECT id FROM public.medications ORDER BY id LIMIT 1) m
ORDER BY c.id
LIMIT 1;

COMMIT;
