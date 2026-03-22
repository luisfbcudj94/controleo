param(
    [string]$ApiBaseUrl = "http://localhost:5051"
)

$ErrorActionPreference = "Stop"

function Parse-Amount([string]$raw) {
    $clean = $raw -replace '\$', '' -replace '\s', ''
    $isNegative = $clean.StartsWith('-')
    $clean = $clean.TrimStart('-')
    $clean = $clean -replace '\.', ''
    $clean = $clean -replace ',', '.'

    $value = [decimal]::Parse($clean, [System.Globalization.CultureInfo]::InvariantCulture)
    if ($isNegative) {
        return -$value
    }

    return $value
}

Write-Host "Purgando datos actuales..." -ForegroundColor Cyan
Invoke-RestMethod -Uri "$ApiBaseUrl/api/admin/purge-data" -Method Post | Out-Null

$rawData = @'
Marzo
Fecha	Descripción	Tipo de molvimiento	Valor	Medio de pago
01/03/2026	Pajarito	Viajes	 $14.000	Efectivo
02/03/2026	Arroz chino aguazul	Salidas	 $44.000	Efectivo
02/03/2026	Gasolina furia	Basicos para vivir	 $35.000	TC Black
02/03/2026	Casa corona	Salidas	 $44.000	TC Black
02/03/2026	Drogueria	Basicos para vivir	 $43.000	Efectivo
02/03/2026	Comida	Salidas	 $46.000	Efectivo
03/03/2026	Desayuno y cena herbalife (almuerzo)	Basicos para vivir	 $36.000	Transferencia
03/03/2026	Pago natura	Bienestar	 $58.000	Nequi
03/03/2026	Chorizos barrio yopal	Salidas	 $32.000	Efectivo
03/03/2026	Drogueria	Basicos para vivir	 $14.000	TC Black
04/03/2026	Cosas de aseo	Basicos para vivir	 $17.000	TC Rappi
04/03/2026	Abono maestro	Sogamoso obra	 $1.500.000	Transferencia
04/03/2026	Pago icetex	Deudas	 $1.135.000	Transferencia
04/03/2026	Almacenamiento Luis	Suscripciones	 $13.000	TC Rappi
04/03/2026	Arriendo	Basicos para vivir	 $1.060.000	Transferencia
05/03/2026	Internet casa	Basicos para vivir	 $131.000	TC Black
05/03/2026	Desayuno y cena herbalife (almuerzo)	Basicos para vivir	 $36.000	Transferencia
05/03/2026	Cervezas	Salidas	 $10.000	Efectivo
05/03/2026	Comida barrio	Salidas	 $28.000	Efectivo
06/03/2026	Gasolina furia	Basicos para vivir	 $40.000	TC Black
06/03/2026	Cervezas con Montero	Imprevistos	 $56.000	Transferencia
06/03/2026	Motilada	Basicos para vivir	 $34.000	Transferencia
06/03/2026	Torta con Samuel	Imprevistos	 $18.000	TC Black
07/03/2026	Micheladas mirador aguazul	Imprevistos	 $72.000	Transferencia
07/03/2026	Polas con Natalia	Salidas	 $15.000	Transferencia
08/03/2026	Bebidas viaje	Viajes	 $31.000	TC Black
08/03/2026	Gasolina furia	Viajes	 $26.000	TC Black
08/03/2026	Desayuno	Viajes	 $26.000	Efectivo
08/03/2026	Gasolina furia	Viajes	 $43.000	TC Black
08/03/2026	Bebidas guaduas	Viajes	 $7.000	Efectivo
08/03/2026	Hotel guaduas	Viajes	 $70.000	Efectivo
08/03/2026	Arepas doradal	Viajes	 $74.000	Efectivo
08/03/2026	Gasolina furia	Viajes	 $33.000	TC Black
08/03/2026	Aporte cumpleaños Samuel	Imprevistos	 $36.000	Nequi
08/03/2026	Tamal panaderia	Salidas	 $17.500	Nequi
08/03/2026	Tienda Hanyi	Imprevistos	 $9.100	Nequi
09/03/2026	Mercado carnes	Basicos para vivir	 $134.000	TC Black
09/03/2026	Herbalife	Basicos para vivir	 $273.000	TC Black
09/03/2026	Mercado d1	Basicos para vivir	 $262.000	TC Black
10/03/2026	Pago celular Alejandro	Basicos para vivir	 $61.000	TC Rappi
10/03/2026	Compras compulsivas	Hogar	 $230.000	TC Black
10/03/2026	Plan panchocha	Salidas	 $36.000	TC Black
11/03/2026	Youtube premium	Suscripciones	 $21.000	TC Rappi
11/03/2026	Billard con capibara	Salidas	 $13.000	Efectivo
11/03/2026	Hamburguesas perritos	Salidas	 $52.000	Efectivo
12/03/2026	Pantalon Gef y medias Alejo	Bienestar	 $310.000	TC Black
12/03/2026	Parqueadero	Salidas	 $5.000	TC Black
13/03/2026	Icloud	Suscripciones	 $13.000	TC Black
13/03/2026	Helados Mc donalds	Salidas	 $21.000	TC Black
13/03/2026	Aretes Hanyi	Bienestar	 $35.000	TD Bancolombia
13/03/2026	Parqueadero	Salidas	 $3.000	TD Bancolombia
14/03/2026	Gasolina furia	Basicos para vivir	 $40.000	TC Black
14/03/2026	Polas San Antonio de Pereira	Viajes	 $30.000	Efectivo
14/03/2026	Postres	Viajes	 $22.000	Efectivo
14/03/2026	Parqueadero	Viajes	 $7.000	Efectivo
14/03/2026	Domino's pizza	Salidas	 $42.000	TC Black
15/03/2026	Montallantas	Viajes	 $6.000	Efectivo
15/03/2026	Almuerzo don Matias	Viajes	 $48.000	Efectivo
15/03/2026	Tintos	Viajes	 $12.000	Efectivo
15/03/2026	Parqueadero	Viajes	 $4.000	Efectivo
15/03/2026	Chorizos barrio	Salidas	 $36.000	Efectivo
15/03/2026	Polas con Natalia	Salidas	 $13.000	Efectivo
16/03/2026	Uber yamaha	Basicos para vivir	 $10.000	TC Black
16/03/2026	Devolucion mercado	Basicos para vivir	- $198.000	Transferencia
16/03/2026	Pago servicios publicos	Basicos para vivir	 $85.000	Transferencia
16/03/2026	Fibra herbalife	Basicos para vivir	 $77.000	TC Black
16/03/2026	Rifa Alvaro	Imprevistos	 $10.000	Transferencia
16/03/2026	Uber yamaha	Basicos para vivir	 $13.000	TC Black
16/03/2026	Mantenimiento furia	Basicos para vivir	 $653.000	TC Black
16/03/2026	Salchipapa	Salidas	 $58.000	Efectivo
16/03/2026	Pastas migrañosa	Basicos para vivir	 $25.000	TC Black
17/03/2026	Mercado la vaquita	Basicos para vivir	 $285.000	TC Black
17/03/2026	Mercado d1	Basicos para vivir	 $90.000	TC Black
17/03/2026	Partida confirmación	Basicos para vivir	 $70.000	Transferencia
17/03/2026	Dollarcity	Hogar	 $140.000	TC Black
18/03/2026	Accesorios celular migrañosa	Hogar	 $45.000	Transferencia
18/03/2026	Gasolina furia	Basicos para vivir	 $40.000	TC Black
18/03/2026	Vaquita mercado	Basicos para vivir	 $48.000	TC Black
18/03/2026	Mercado carnes	Basicos para vivir	 $95.000	TC Black
18/03/2026	Arepas Hanyi	Salidas	 $16.000	Efectivo
18/03/2026	Polas con Natalia	Salidas	 $7.000	Efectivo
18/03/2026	Toallas Hanyi	Basicos para vivir	 $7.000	Efectivo
19/03/2026	Pedido herbalife (te, aloe, batido)	Basicos para vivir	 $310.000	TC Black

FEBRERO
Fecha	Descripción	Tipo de molvimiento	Valor	Medio de pago
01/02/2026	Netflix	Suscripciones	 $19.000	TC Black
01/02/2026	Mesa y silla Natalia	Hogar	 $175.000	TC Black
01/02/2026	Accesorio celular Natalia	Bienestar	 $22.000	Nequi
01/02/2026	Parqueadero santa fe	Salidas	 $6.000	TD Bancolombia
01/02/2026	Comida alitas	Salidas	 $56.000	Efectivo
01/02/2026	Granizados, cigarrillos	Salidas	 $32.000	Efectivo
01/02/2026	Regalo navidad Hanyi	Bienestar	 $209.000	TC Black
01/02/2026	Papitas envigado	Salidas	 $51.000	Efectivo
02/02/2026	Pago arriendo	Basicos para vivir	 $1.060.000	Transferencia
02/02/2026	Herbalife	Basicos para vivir	 $407.000	TC Black
02/02/2026	Mercado carne (semanal)	Basicos para vivir	 $205.000	TC Black
02/02/2026	Gasolina furia	Basicos para vivir	 $31.000	TC Black
02/02/2026	Mercado semanal d1	Basicos para vivir	 $294.000	TC Black
02/02/2026	Chorizos del barrio	Salidas	 $28.000	Transferencia
02/04/2026	Collar Natalia	Bienestar	 $30.000	Nequi
02/04/2026	Entradas cine	Salidas	 $24.000	TC Black
02/04/2026	Espejo Natalia	Hogar	 $15.000	TC Black
02/04/2026	Parqueadero cine	Salidas	 $4.000	TD Bancolombia
02/04/2026	Perritos en el poblado	Salidas	 $57.000	Transferencia
02/04/2026	Granizados barrio	Salidas	 $18.000	Efectivo
02/05/2026	Almacenamiento google	Suscripciones	 $13.000	TC Rappi
02/05/2026	Internet casa	Basicos para vivir	 $130.000	TC Black
02/05/2026	Credito davivienda	Deudas	 $2.510.000	Transferencia
02/05/2026	Icetex	Deudas	 $1.135.000	Transferencia
02/06/2026	Alitas barrio	Salidas	 $68.000	Transferencia
02/06/2026	Billard con capibara	Salidas	 $18.000	Transferencia
07/02/2026	Salida hachas	Viajes	 $185.000	Efectivo
08/02/2026	Pizza dominos pijamada	Salidas	 $42.000	TC Black
08/02/2026	Cervecitas	Salidas	 $23.000	Efectivo
08/02/2026	Mercado d1	Basicos para vivir	 $222.000	TC Black
09/02/2026	Suscripción almacenamiento de google	Suscripciones	 $9.000	TC Rappi
09/02/2026	Mercado carnes	Basicos para vivir	 $166.000	TC Black
09/02/2026	Herbalife aloe	Basicos para vivir	 $95.000	TC Black
09/02/2026	Mercado Devolucion	Basicos para vivir	- $400.000	Transferencia
10/02/2026	Pago celular Alejo	Basicos para vivir	 $61.000	TC Rappi
11/02/2026	Youtube premium	Suscripciones	 $21.000	TC Rappi
11/02/2026	Advil pastas migrañosa	Basicos para vivir	 $15.000	Efectivo
11/02/2026	Helado y takis	Salidas	 $12.000	Efectivo
12/02/2026	Servicios publicos	Basicos para vivir	 $104.000	Transferencia
12/02/2026	Gasolina furia	Basicos para vivir	 $31.000	TC Black
13/02/2026	Hamburguesas el corral	Salidas	 $88.000	TC Black
13/02/2026	Espacio celular Natalia	Suscripciones	 $13.000	TC Black
13/02/2026	Cejas Hanyi	Basicos para vivir	 $38.000	Nequi
13/02/2026	Helado oreo	Salidas	 $22.000	TC Black
14/02/2026	Polas con Caro	Salidas	 $60.000	Transferencia
14/02/2026	Entradas comfama	Viajes	 $64.000	TC Black
14/02/2026	Postres San Antonio	Viajes	 $24.000	Transferencia
14/02/2026	Almuerzos comfama	Viajes	 $65.000	TC Black
14/02/2026	Aretes	Bienestar	 $2.000	Efectivo
14/02/2026	Cafecitos San Antonio	Viajes	 $29.000	Transferencia
14/02/2026	Parqueadero	Viajes	 $14.000	Efectivo
15/02/2026	Mercado d1	Basicos para vivir	 $231.000	TC Black
15/02/2026	Aceite de coco y vinagre	Basicos para vivir	 $15.000	TC Black
15/02/2026	Hamburguesitas	Salidas	 $52.000	Efectivo
16/02/2026	Uber para llevar moto a mantenimiento	Basicos para vivir	 $12.000	TC Black
16/02/2026	Uber recoger moto	Basicos para vivir	 $9.000	TC Black
16/02/2026	Mantenimiento furia verde	Basicos para vivir	 $176.000	TC Black
16/02/2026	Lavada furia verde	Basicos para vivir	 $40.000	Efectivo
16/02/2026	Cositas miniso	Hogar	 $37.000	TC Black
16/02/2026	Lili pink	Bienestar	 $30.000	TC Black
16/02/2026	Parqueadero	Salidas	 $6.000	TD Bancolombia
16/02/2026	Papitas	Salidas	 $49.000	Transferencia
18/02/2026	Mercado carnes	Basicos para vivir	 $164.000	TC Black
18/02/2026	Shampoos Hanyi	Basicos para vivir	 $107.000	TC Black
18/02/2026	Polas con capi	Salidas	 $10.000	Efectivo
18/02/2026	Polas con Hanyi	Salidas	 $16.000	Efectivo
18/02/2026	Papitas barrio	Salidas	 $35.000	Efectivo
19/02/2026	Pedido aloe	Basicos para vivir	 $349.000	TC Black
19/02/2026	Garguerias	Salidas	 $21.000	TC Black
19/02/2026	Inyeccion hanyi y pastas	Basicos para vivir	 $38.000	Efectivo
20/02/2026	Cervecitas	Salidas	 $48.000	Efectivo
20/02/2026	Hamburguesa perritos	Salidas	 $54.000	Efectivo
21/02/2026	Carne asada	Salidas	 $112.000	TC Black
21/02/2026	Loneta bershka	Bienestar	 $101.000	TC Black
21/02/2026	Karibik	Bienestar	 $205.000	TC Black
21/02/2026	Gasolina furia	Basicos para vivir	 $42.000	TC Black
23/02/2026	Mercado d1	Basicos para vivir	 $279.000	TC Black
23/2/2026	Prestamo nain	Prestamo	 $50.000	Nequi
23/2/2026	Celular Natalia	Basicos para vivir	 $46.000	TC Rappi
23/02/2026	Gorra Samuel	Imprevistos	 $100.000	TC Black
23/02/2026	Parqueadero	Salidas	 $5.000	TC Black
23/02/2026	Salchipapa	Salidas	 $48.000	TC Black
23/02/2026	Miniso	Hogar	 $40.000	TC Black
23/02/2026	Airbnb	Viajes	 $115.000	TC Rappi
24/02/2026	Cambio de airbnb	Viajes	 $3.000	TC Rappi
24/02/2026	Pastas gripe	Basicos para vivir	 $27.000	Transferencia
24/02/2026	True caller	Suscripciones	 $5.000	TC Rappi
24/02/2026	Rappi pro	Suscripciones	 $13.000	TC Rappi
25/02/2026	Estacionarias e impermeables	Basicos para vivir	 $82.000	TC Black
25/02/2026	Arepas choclo	Salidas	 $10.000	Efectivo
25/02/2026	Gasolina furia	Basicos para vivir	 $10.000	TC Black
25/02/2026	Pastas gripe	Basicos para vivir	 $8.000	Efectivo
25/02/2026	Bebidas y pastas viaje	Viajes	 $27.000	Efectivo
25/02/2026	Chorizos del barrio	Viajes	 $28.000	Efectivo
26/02/2026	Gasolina furia	Viajes	 $29.000	TC Black
26/02/2026	Bebidas camino	Viajes	 $16.000	Efectivo
26/02/2026	Gasolina furia	Viajes	 $38.000	TC Black
26/02/2026	Airbnb zipaquira	Viajes	 $117.000	TC Rappi
26/02/2026	Arepas la vega	Viajes	 $32.000	Efectivo
26/02/2026	Comida zipaquirá	Viajes	 $44.000	Efectivo
26/02/2026	OXXO bebidas	Viajes	 $20.000	TC Black
26/02/2026	Misa abuelita Almeida	Basicos para vivir	 $26.000	Efectivo
27/02/2026	Parqueadero catedral	Viajes	 $13.000	TC Black
27/02/2026	Desayuno zipaquira	Viajes	 $30.000	Efectivo
27/02/2026	Entradas catedral de sal	Viajes	 $150.000	TC Black
27/02/2026	Helados catedral	Viajes	 $18.000	Efectivo
27/02/2026	Frisby zipaquirá	Viajes	 $66.000	TC Black
27/02/2026	Parqueadero zipaquirá	Viajes	 $8.000	Transferencia
27/02/2026	Politas zipaquirá	Viajes	 $81.000	TC Black
27/02/2026	Gasolina furia	Viajes	 $31.000	TC Black
27/02/2026	OXXO bebidas	Viajes	 $27.000	TC Black
28/02/2026	Netflix	Suscripciones	 $19.000	TC Black
28/02/2026	Sogamoso obra	Sogamoso obra	 $6.252.000	TC Black
28/02/2026	Noraver	Basicos para vivir	 $10.000	Efectivo
28/02/2026	Abono maestro	Sogamoso obra	 $2.000.000	Transferencia
28/02/2026	Drogueria	Basicos para vivir	 $38.000	TC Black
28/02/2026	Comida sogamoso	Viajes	 $87.000	TC Black
28/02/2026	Cervecita sogamoso	Viajes	 $12.000	Efectivo
28/02/2026	Airbn sogamoso	Viajes	 $105.000	TC Rappi
28/02/2026	Gasolina furia	Viajes	 $29.000	TC Black
21/02/2026	Abono sogamoso maestro	Sogamoso obra	 $1.500.000	Transferencia
28/02/2026	Retorno mercado	Basicos para vivir	- $380.000	Transferencia

ENERO
Fecha	Descripción	Tipo de molvimiento	Valor	Medio de pago
01/01/2026	Almuerzo sancocho con papá y hermanos	Salidas	 $50.000	Efectivo
02/01/2026	Aloe y batido	Basicos para vivir	 $178.000	TC Black
02/01/2026	Arriendo	Basicos para vivir	 $1.060.000	Transferencia
02/01/2026	Icetex	Deudas	 $1.135.000	Transferencia
02/01/2026	Gasolina moto	Basicos para vivir	 $34.000	TD Bancolombia
02/01/2026	Decoración y torta Samuel	Salidas	 $59.000	TD Bancolombia
02/01/2026	Empanadas con Natalia	Salidas	 $13.000	Transferencia
02/01/2026	Cervezas	Salidas	 $15.000	Transferencia
03/01/2026	Recarga Sra.Sandra	Salidas	 $8.500	TC Rappi
03/01/2026	Comida viaje trinidad	Salidas	 $15.000	Transferencia
03/01/2026	Gasolina moto	Basicos para vivir	 $30.000	TC Black
04/01/2026	Almacenamiento google	Suscripciones	 $13.000	TC Rappi
04/01/2026	Almuerzo	Basicos para vivir	 $15.000	Transferencia
04/01/2026	Salida con Samuel	Salidas	 $13.000	Efectivo
04/01/2026	Agua	Basicos para vivir	 $4.500	Efectivo
05/01/2026	Pago internet	Basicos para vivir	 $131.000	TC Black
05/01/2026	Papel higienico	Basicos para vivir	 $5.000	Efectivo
05/01/2026	Almuerzo	Basicos para vivir	 $20.000	Efectivo
05/01/2026	Pago credito bancolombia	Deudas	 $480.000	Transferencia
05/01/2026	Gasolina moto	Basicos para vivir	 $26.000	TC Black
05/01/2026	Hotel	Salidas	 $63.000	Efectivo
05/01/2026	Comida	Salidas	 $55.000	Efectivo
05/01/2026	Cervezas	Salidas	 $25.000	Efectivo
06/01/2026	Empanadas	Salidas	 $10.000	Transferencia
06/01/2026	Postres	Salidas	 $26.000	TC Black
06/01/2026	Cervezas	Salidas	 $15.000	Efectivo
07/01/2026	YouTube	Suscripciones	 $9.000	TC Rappi
08/01/2026	Uñas Natalia	Bienestar	 $30.000	Nequi
08/01/2026	Cosas de aseo	Basicos para vivir	 $6.000	TD Bancolombia
08/01/2026	Almuerzo	Basicos para vivir	 $20.000	Efectivo
08/01/2026	Postres (Alejandro y Samuel)	Salidas	 $23.000	TC Black
09/01/2026	Internet celular	Basicos para vivir	 $61.000	TC Rappi
09/01/2026	Cambio aceite moto	Basicos para vivir	 $134.000	TC Black
09/01/2026	Cigarrillos	Salidas	 $9.000	Nequi
09/01/2026	Cervezas	Salidas	 $36.000	TC Black
09/01/2026	Comida rapi roy	Salidas	 $84.000	TC Black
09/01/2026	Gasolina moto	Basicos para vivir	 $36.000	TC Black
10/01/2026	Desodorante Natalia	Basicos para vivir	 $20.000	Nequi
10/01/2026	Salida cervezas esquina aguazul	Salidas	 $27.000	Efectivo
10/01/2026	Gasolina moto	Basicos para vivir	 $34.000	TC Black
10/01/2026	Gasolina moto	Viajes	 $10.000	Efectivo
11/01/2026	Parada a comer viaje (Aguazul-Medellin)	Viajes	 $15.800	Efectivo
11/01/2026	Mecato	Viajes	 $23.000	Efectivo
11/01/2026	Soda	Viajes	 $4.000	Efectivo
11/01/2026	Gasolina moto	Viajes	 $31.000	TC Black
11/01/2026	Hotel Guaduas	Viajes	 $70.000	Efectivo
11/01/2026	Youtube premium	Suscripciones	 $21.000	TC Rappi
11/01/2026	Empanadas Alejandro y Hanyi	Viajes	 $24.000	Efectivo
11/01/2026	Electrolit gaseosas Guaduas	Viajes	 $20.000	Efectivo
12/01/2026	Mecato Guaduas	Viajes	 $13.000	Efectivo
12/01/2026	Almuerzo fogon paisa	Viajes	 $88.000	Efectivo
12/01/2026	Bebida mirador nublado	Viajes	 $10.000	Efectivo
12/01/2026	Suscripcion twitch	Suscripciones	 $9.000	Efectivo
12/01/2026	Gasolina moto	Viajes	 $41.000	TC Black
13/01/2026	Pedido herbalife	Basicos para vivir	 $480.000	TC Black
13/01/2026	Gasolina furia verde	Basicos para vivir	 $27.000	TC Black
13/01/2026	Mercado verdura	Basicos para vivir	 $84.000	TC Black
13/01/2026	Hamburguesas perritos	Salidas	 $58.000	Efectivo
14/01/2026	Mercado carnes	Basicos para vivir	 $108.000	TC Black
14/01/2026	Prestamo no pago	Imprevistos	 $5.000	Nequi
14/01/2026	Mercado D1	Basicos para vivir	 $233.000	TC Black
14/01/2026	Lavada moto	Basicos para vivir	 $8.000	Efectivo
14/01/2026	Pago celular Natalia	Basicos para vivir	 $44.000	TC Rappi
15/01/2025	Herbalife - proteina	Basicos para vivir	 $116.000	TC Black
16/01/2025	Mouse gamer	Hogar	 $180.000	Transferencia
16/01/2025	Helados mimos	Salidas	 $16.000	Efectivo
16/01/2025	Micheladas palmas	Salidas	 $26.000	Efectivo
16/01/2025	Inyección anti bebes	Basicos para vivir	 $24.000	Efectivo
16/01/2025	Polas d1 dia gamer	Salidas	 $18.000	TC Black
17/01/2025	Notaria compañera permanente	Imprevistos	 $35.000	Efectivo
17/01/2025	Almuerzo crepes celebración compañeros permanentes	Salidas	 $109.000	TC Black
17/01/2025	Papitas envigado	Salidas	 $51.000	Transferencia
17/01/2025	Jugo naranja cocteles y cervezas en carulla palmas	Salidas	 $22.000	TC Black
18/01/2025	3 Hamburguesas perritos	Salidas	 $80.000	Efectivo
18/01/2025	Granizados	Salidas	 $30.000	Efectivo
20/01/2025	Matricula Laura	Imprevistos	 $1.370.000	TC Black
20/01/2025	Mercado papá (retorno de dinero)	Basicos para vivir	- $140.000	Transferencia
20/1/2025	Mercado D1	Basicos para vivir	 $216.000	TC Black
20/1/2025	Mercado carnes	Basicos para vivir	 $104.000	TC Black
22/01/2025	Aloe y batido	Basicos para vivir	 $178.000	TC Black
22/01/2025	Gasolina furia verde	Basicos para vivir	 $31.000	TC Black
22/01/2025	Loneta Lucho	Bienestar	 $100.000	TC Black
22/01/2025	Parqueadero centro comercial	Salidas	 $6.000	TC Black
22/01/2025	Papitas Envigado	Salidas	 $31.000	Efectivo
23/01/2026	Mercado papá (retorno de dinero)	Basicos para vivir	- $106.000	Transferencia
24/01/2025	Tragos en casa (aguardiente amarillo, real y cigarrillos)	Salidas	 $70.000	Efectivo
24/01/2025	Pollo asado (almuerzo)	Salidas	 $61.000	Transferencia
24/01/2025	Cocienrto Bad Bunny (Tour DtMF)	Salidas	 $1.000.000	Transferencia
24/01/2025	Cocienrto Bad Bunny (Tour DtMF)	Salidas	 $1.200.000	Efectivo
25/01/2025	Coca cola d1 (cena)	Salidas	 $7.000	TD Bancolombia
26/01/2026	True caller	Suscripciones	 $5.000	TC Rappi
26/01/2026	Motilada	Bienestar	 $15.000	Efectivo
26/01/2026	Mercado carnes	Basicos para vivir	 $88.000	TC Black
26/01/2026	Mercado D1	Basicos para vivir	 $176.000	TC Black
27/01/2026	Granizados	Salidas	 $20.000	Efectivo
28/01/2026	Cuota funeraria	Basicos para vivir	 $159.000	Transferencia
28/01/2026	Prestamo Nain	Prestamo	 $50.000	Nequi
28/01/2026	Granizados	Salidas	 $28.000	Efectivo
28/01/2026	Salchipapa barrio	Salidas	 $33.000	Efectivo
30/01/2026	Hamburguesas tierra querida	Salidas	 $65.000	TD Bancolombia
30/01/2026	Granizados	Salidas	 $24.000	Efectivo
30/01/2026	Cigarrillos	Salidas	 $8.000	Efectivo
30/01/2026	Mercado papá (retorno de dinero)	Basicos para vivir	- $88.000	Transferencia
'@

$movementPattern = 'Basicos para vivir|Hogar|Salidas|Imprevistos|Suscripciones|Deudas|Prestamo|Bienestar|Viajes|Sogamoso obra|-'
$paymentPattern = 'TC Black|TC Rappi|TD Bancolombia|TC Nu|Efectivo|Transferencia|Nequi|-'
$linePattern = '^(?<date>\d{1,2}/\d{1,2}/\d{4})\s+(?<desc>.+?)\s+(?<movement>' + $movementPattern + ')\s+(?<value>-?\s*\$?\s*[\d\.,]+)\s+(?<payment>' + $paymentPattern + ')$'

$lines = $rawData -split "`r?`n"
$inserted = 0
$failed = 0

foreach ($line in $lines) {
    $text = ($line -replace "`t", ' ').Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { continue }
    if ($text -match '^(Marzo|FEBRERO|ENERO)$') { continue }
    if ($text -match '^Fecha\s+') { continue }

    $text = ($text -replace '\s{2,}', ' ').Trim()

    if ($text -notmatch $linePattern) {
        Write-Host "No parseado: $text" -ForegroundColor Yellow
        $failed++
        continue
    }

    try {
        $date = [DateTime]::ParseExact($matches.date, 'd/M/yyyy', [System.Globalization.CultureInfo]::InvariantCulture)
        $amount = Parse-Amount $matches.value

        $payload = @{
            date = $date.ToString('yyyy-MM-dd')
            description = $matches.desc.Trim()
            movementType = $matches.movement.Trim()
            amount = $amount
            paymentMethod = $matches.payment.Trim()
        } | ConvertTo-Json

        Invoke-RestMethod -Uri "$ApiBaseUrl/api/expenses" -Method Post -Body $payload -ContentType "application/json" | Out-Null
        $inserted++
    }
    catch {
        Write-Host "Error insertando: $text" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor DarkRed
        $failed++
    }
}

Write-Host "Insertados: $inserted" -ForegroundColor Green
Write-Host "Fallidos:   $failed" -ForegroundColor Yellow
