Developer - Charlie Baker 
Institution - University of Leeds
SID - 201396850
Date - 30th April 2025

TO EDIT ANY CONTROL VARIABLE COSTS OR DEFAULT VALUES, EDIT THE SCRIPTABLE OBJECT IN EITHER THE SENSORS, EXCRETERS, ELIMINATORS, OR SYSTEM CONTROLS FOLDER. 

TO CREATE A NEW SENSOR, COPY AND PASTE ANOTHER SCRIPTABLE OBJECT IN THE SENSORS FOLDER, ASSIGN VALUES, AND DRAG IT INTO THE SETUPEXPERIMENT SENSORS ARRAY.
TO CREATE A NEW EXCRETER, COPY AND PASTE ANOTHER SCRIPTABLE OBJECT IN THE EXCRETERS FOLDER, ASSIGN VALUES, AND DRAG IT INTO THE SETUPEXPERIMENT EXCRETERS ARRAY.
TO CREATE A NEW ELIMINATOR, COPY AND PASTE ANOTHER SCRIPTABLE OBJECT IN THE ELIMINATORS FOLDER, ASSIGN VALUES, AND DRAG IT INTO THE SETUPEXPERIMENT ELIMINATORS ARRAY.

TO CREATE A NEW SYSTEM CONTROL, COPY AND PASTE ANOTHER SCRIPTABLE OBJECT IN THE SYSTEM CONTROLS FOLDER, ASSIGN VALUES, AND DRAG IT INTO THE SETUPEXPERIMENT SYSTEMCONTROLS ARRAY. 
THEN ADD THE NEW BEHAVIOUR LOGIC IN CONTROLPARAMETER.CS, IN SIMULATION.CS, AND IN MASTERALGORITHM.CS.

Total startup costs are calculated:
- Add the startup costs of all the components in the system.
- Add the manufacture cost of the bioreactor.
- Multiply the result by the number of bioreactors.

Est. weekly continuation costs are calculated:
- Sum the continuation costs of all the components in the system.
- Multiply the supplyCost per unit of each excreter by the excretion rate and multiply by 7 to convert to weekly (understimate).
- Multiply the calf serum cost by the system fluid volume and the media exchange percentage. Divide by the media exchange rate. Multiply the result by 1.68 to account for hours to weeks and percentages. (overstimate).
- Sum the above calculations and mutiply by the number of bioreactors.

This is applied to lower and upper bounds of the supplyCosts and the media exchange rate and exchange percentage to give a range of costs.

Example:
- £100 continuation costs
- £50 per unit of excreter, 0.5 units per day. = 50 * 0.5 * 7 = £175 per week.
- £265 per litre, 0.25 litres, 50% exchange, every 10 hours = 265 * 0.25 * 50 * 1.68 / 10 = £556.50 per week.
- 5 bioreactors
- Total = (100 + 175 + 556.5) * 5 = £4157.50 per week

If not defined, the following system control variables are set to default values, as from literature review:

- Media exchange percentage: 50%
- Media exchange range: 10 hours minimum, 84 hours maximum
- Temperature: 37.0 degrees celcius
- Initial Eccentricity: 0.7
- Final Eccentricity: 0.9
- Initial RPM: 60
- Final RPM: 120
- Initial MotorOn Duration: 15 minutes
- Final MotorOn Duration: 45 minutes
- Initial MotorOff Duration: 45 minutes
- Final MotorOff Duration: 15 minutes
- Daily Rest Period: 8 Hours
- Initial Static Period: 7 days
