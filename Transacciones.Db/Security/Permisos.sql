-- Permisos y usuarios.
--
-- En Azure SQL Database los usuarios se crean por el portal o por T-SQL sobre
-- la base master; el servidor y los roles se gestionan fuera del proyecto. Aqui
-- solo se conceden permisos dentro del esquema.
--
-- Principio de minimo privilegio: un usuario de aplicacion NO debe ser dbo, y
-- solo lee/escribe en dbo. Nada de db_owner.

CREATE USER [transacciones_app] FOR LOGIN [transacciones_app]
GO

CREATE USER [transacciones_lectura] FOR LOGIN [transacciones_lectura]
GO

-- La aplicacion necesita leer y escribir, y ejecutar los procedimientos.
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [transacciones_app]
GO

GRANT EXECUTE ON SCHEMA::dbo TO [transacciones_app]
GO

-- Solo lectura: es el usuario para informes y soporte.
GRANT SELECT ON SCHEMA::dbo TO [transacciones_lectura]
GO