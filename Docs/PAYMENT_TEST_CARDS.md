# Payment test cards

`SimulatedGatewayService` decides from the last four digits only. The full card number is checked with the Luhn algorithm and is never stored or sent to the gateway. Latency is a small random delay from `Payments:GatewayMinDelayMs` and `Payments:GatewayMaxDelayMs`. Tests inject a delay that returns immediately. The gateway call is not retried.

| Last 4 | Result | Card number | Expiry | CVV |
| --- | --- | --- | --- | --- |
| 1111 | Approved | `4111111111111111` | any future month | `123` |
| 0002 | Declined. The payment row is `Failed` with reason `Card declined`. | `4000000000000002` | any future month | `123` |
| 9999 | Gateway timeout. The payment row is `Failed` with reason `Gateway timed out`, and the API returns HTTP 504. | `4111111111119999` | any future month | `123` |

Any other Luhn-valid number is declined. An invalid Luhn number or an expiry in the past is rejected with HTTP 400 before the gateway is called.
