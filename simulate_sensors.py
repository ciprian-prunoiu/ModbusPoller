# simulate_sensors.py - varianta dramatica
import time
import math
import random
from pymodbus.client import ModbusTcpClient

SLAVE_IP = "127.0.0.1"
SLAVE_PORT = 502
SLAVE_ID = 1

client = ModbusTcpClient(SLAVE_IP, port=SLAVE_PORT)
client.connect()

t = 0
while True:
    # Fiecare registru cu frecventa si amplitudine diferita
    temperatura = int(500 + 400 * math.sin(t / 8.0) + 100 * math.sin(t / 2.0))
    presiune    = int(2000 + 800 * math.sin(t / 15.0) + random.uniform(-50, 50))
    umiditate   = int(500 + 300 * math.sin(t / 5.0) + 100 * math.cos(t / 3.0))
    nivel       = int(500 + 450 * math.sin(t / 20.0))
    debit       = int(1000 + 500 * math.sin(t / 3.0) + 200 * math.cos(t / 1.5))
    tensiune    = int(5000 + 1500 * math.sin(t / 10.0))
    curent      = int(800 + 400 * math.sin(t / 4.0) + random.uniform(-30, 30))
    rpm         = int(3000 + 1200 * math.sin(t / 12.0) + 300 * math.cos(t / 2.5))
    vibratie    = int(500 + 300 * math.sin(t / 2.0))
    if random.random() < 0.08:
        vibratie = random.randint(1500, 2500)  # spike-uri

    temp_ulei   = int(1500 + 700 * math.sin(t / 25.0))

    valori = [max(0, v) for v in [temperatura, presiune, umiditate, nivel, debit,
                                    tensiune, curent, rpm, vibratie, temp_ulei]]

    client.write_registers(0, valori, slave=SLAVE_ID)
    print(f"[{t:4d}] " + "  ".join(f"{v:5d}" for v in valori))
    t += 1
    time.sleep(1)