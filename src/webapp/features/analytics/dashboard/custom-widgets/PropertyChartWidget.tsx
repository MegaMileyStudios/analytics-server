import { Button } from "@components/Button";
import { Popover, PopoverContent, PopoverTrigger } from "@components/Popover";
import { ToggleGroup, ToggleGroupList, ToggleGroupTrigger } from "@components/ToggleGroup";
import { LineChart, StackMode } from "@features/analytics/dashboard/LineChart";
import { EventNameFilterDropdown } from "@features/analytics/sessions/filters/EventNameFilterDropdown";
import { useApps } from "@features/apps";
import { useChartColors } from "@features/theme";
import { formatPeriod } from "@fns/format-date";
import { formatNumber } from "@fns/format-number";
import { IconMenu2, IconPlus, IconX } from "@tabler/icons-react";
import { useQueries, useQuery } from "@tanstack/react-query";
import { useAtomValue, useSetAtom } from "jotai";
import { useMemo } from "react";
import { dateFilterValuesAtom } from "../../../../atoms/date-atoms";
import {
  dashboardWidgetsAtom,
  PropertyAggregation,
  PropertyChartWidgetConfig,
  PropertySeries,
  SingleWidgetConfig,
} from "../../../../atoms/widgets-atoms";
import {
  eventPropDistribution,
  eventPropDistributionPeriodic,
  eventPropKeys,
  eventPropNumeric,
  EventPropKey,
  EventPropNumericRow,
  Granularity,
} from "../../query";
import { CategoryChart, CategoryDisplay } from "./CategoryChart";
import { CategoryTimeChart } from "./CategoryTimeChart";

type Props = {
  appId: string;
  appName: string;
  widgetConfig: SingleWidgetConfig<PropertyChartWidgetConfig>;
};

const AGGREGATIONS: { value: PropertyAggregation; label: string }[] = [
  { value: "avg", label: "Average" },
  { value: "median", label: "Median" },
  { value: "min", label: "Min" },
  { value: "max", label: "Max" },
  { value: "sum", label: "Sum" },
  { value: "p95", label: "95th percentile" },
  { value: "p99", label: "99th percentile" },
];

const aggregationLabel = (aggregation: PropertyAggregation) =>
  AGGREGATIONS.find((x) => x.value === aggregation)?.label ?? aggregation;

const seriesLabel = (series: PropertySeries) => `${aggregationLabel(series.aggregation)} of ${series.propKey}`;

const seriesValue = (row: EventPropNumericRow, aggregation: PropertyAggregation): number | null =>
  row.events === 0 && aggregation !== "sum" ? null : row[aggregation];

export function PropertyChartWidget(props: Props) {
  const { buildMode } = useApps();
  const colors = useChartColors();
  const setWidgetsConfig = useSetAtom(dashboardWidgetsAtom);
  const { startDateIso, endDateIso, granularity } = useAtomValue(dateFilterValuesAtom);
  const config = props.widgetConfig.properties ?? {};
  const isCategory = config.kind === "boolean" || config.kind === "string";
  const isConfigured =
    !!config.eventName && ((config.kind === "number" && !!config.series?.length) || (isCategory && !!config.propKey));

  const update = (properties: PropertyChartWidgetConfig) => {
    setWidgetsConfig({
      widgetId: props.widgetConfig.id,
      type: "update-properties",
      properties,
      appId: props.appId,
    });
  };

  const changeEvent = (eventName: string | undefined) => {
    if (eventName === config.eventName) return;
    update({ eventName });
  };

  const selectProperty = (key: EventPropKey) => {
    if (key.type === "number") {
      update({
        eventName: config.eventName,
        kind: "number",
        series: [{ propKey: key.key, aggregation: "avg", color: colors.series[0] }],
        stackMode: "none",
      });
    } else {
      update({ eventName: config.eventName, kind: key.type, propKey: key.key, display: config.display ?? "bar" });
    }
  };

  const { data: keys } = useQuery({
    queryKey: ["event-prop-keys", buildMode, props.appId, startDateIso, endDateIso, config.eventName],
    queryFn: () =>
      eventPropKeys({
        buildMode,
        appId: props.appId,
        startDate: startDateIso,
        endDate: endDateIso,
        eventName: config.eventName,
      }),
    staleTime: 10000,
    enabled: !!startDateIso && !!endDateIso && !!config.eventName,
  });

  const settings = (
    <div className="flex flex-col gap-3">
      <EventNameFilterDropdown
        appId={props.appId}
        onValueChange={changeEvent}
        localValue={config.eventName}
        localPersistence
        className="w-[200px]"
      />
      {config.eventName && !config.kind && (
        <PropertySelect keys={keys ?? []} onChange={selectProperty} placeholder="Select Property" />
      )}
      {config.eventName && isCategory && (
        <>
          <div className="flex gap-2 items-center">
            <PropertySelect
              keys={(keys ?? []).filter((x) => x.type !== "number")}
              value={{ key: config.propKey ?? "", type: config.kind! }}
              onChange={selectProperty}
            />
            <Button variant="ghost" size="xs" onClick={() => update({ eventName: config.eventName })}>
              <IconX className="h-4 w-4" />
            </Button>
          </div>
          <ToggleGroup
            value={config.display ?? "bar"}
            onValueChange={(display) => update({ ...config, display: display as CategoryDisplay })}
          >
            <ToggleGroupList>
              <ToggleGroupTrigger value="bar">Bar</ToggleGroupTrigger>
              <ToggleGroupTrigger value="bar-time">Bar over time</ToggleGroupTrigger>
              <ToggleGroupTrigger value="pie">Pie</ToggleGroupTrigger>
            </ToggleGroupList>
          </ToggleGroup>
          {config.display === "bar-time" && (
            <StackModeToggle
              value={config.stackMode ?? "stacked"}
              onChange={(stackMode) => update({ ...config, stackMode })}
            />
          )}
        </>
      )}
      {config.eventName && config.kind === "number" && (
        <NumericSeriesEditor
          series={config.series ?? []}
          stackMode={config.stackMode ?? "none"}
          numericKeys={(keys ?? []).filter((x) => x.type === "number").map((x) => x.key)}
          onChange={(series, stackMode) =>
            update(series.length ? { ...config, series, stackMode } : { eventName: config.eventName })
          }
        />
      )}
    </div>
  );

  if (!isConfigured) {
    return settings;
  }

  return (
    <>
      <div className="flex justify-between items-center gap-2 pb-3">
        <p className="text-sm font-medium truncate">
          {config.eventName}
          {isCategory && <span className="text-muted-foreground"> · {config.propKey}</span>}
        </p>
        <Popover>
          <PopoverTrigger asChild>
            <Button variant="ghost" size="xs" className="h-7 w-7 p-0" aria-label="Chart settings">
              <IconMenu2 className="h-4 w-4" />
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" className="w-auto max-w-[90vw]">
            {settings}
          </PopoverContent>
        </Popover>
      </div>
      {config.kind === "number" && (
        <NumericPropertyChart
          appId={props.appId}
          eventName={config.eventName!}
          series={config.series ?? []}
          stackMode={config.stackMode ?? "none"}
          granularity={granularity}
        />
      )}
      {isCategory && (
        <CategoryPropertyChart
          appId={props.appId}
          eventName={config.eventName!}
          propKey={config.propKey!}
          display={config.display ?? "bar"}
          stackMode={config.stackMode ?? "stacked"}
          granularity={granularity}
        />
      )}
    </>
  );
}

function StackModeToggle(props: { value: StackMode; onChange: (value: StackMode) => void }) {
  return (
    <ToggleGroup value={props.value} onValueChange={(value) => props.onChange(value as StackMode)}>
      <ToggleGroupList>
        <ToggleGroupTrigger value="none">Direct</ToggleGroupTrigger>
        <ToggleGroupTrigger value="stacked">Stacked</ToggleGroupTrigger>
        <ToggleGroupTrigger value="filled">Filled</ToggleGroupTrigger>
      </ToggleGroupList>
    </ToggleGroup>
  );
}

function PropertySelect(props: {
  keys: EventPropKey[];
  value?: EventPropKey;
  placeholder?: string;
  onChange: (key: EventPropKey) => void;
}) {
  const groups: { type: EventPropKey["type"]; label: string }[] = [
    { type: "number", label: "Numbers" },
    { type: "boolean", label: "Booleans" },
    { type: "string", label: "Strings" },
  ];
  const toValue = (key: EventPropKey) => `${key.type}:${key.key}`;

  return (
    <select
      className="form-select compact min-w-[10rem]"
      value={props.value ? toValue(props.value) : ""}
      onChange={(e) => {
        const key = props.keys.find((x) => toValue(x) === e.currentTarget.value);
        if (key) props.onChange(key);
      }}
    >
      {!props.value && (
        <option value="" disabled>
          {props.keys.length ? props.placeholder : "No properties"}
        </option>
      )}
      {groups.map((group) => {
        const keys = props.keys.filter((x) => x.type === group.type);
        if (keys.length === 0) return null;
        return (
          <optgroup key={group.type} label={group.label}>
            {keys.map((key) => (
              <option key={toValue(key)} value={toValue(key)}>
                {key.key}
              </option>
            ))}
          </optgroup>
        );
      })}
    </select>
  );
}

function NumericSeriesEditor(props: {
  series: PropertySeries[];
  stackMode: StackMode;
  numericKeys: string[];
  onChange: (series: PropertySeries[], stackMode: StackMode) => void;
}) {
  const colors = useChartColors();

  const changeSeries = (index: number, change: Partial<PropertySeries>) =>
    props.onChange(
      props.series.map((x, i) => (i === index ? { ...x, ...change } : x)),
      props.stackMode
    );

  const addSeries = () =>
    props.onChange(
      [
        ...props.series,
        {
          propKey: props.series[0]?.propKey ?? props.numericKeys[0],
          aggregation: "avg",
          color: colors.series[props.series.length % colors.series.length],
        },
      ],
      props.stackMode
    );

  const keyOptions = [...new Set([...props.numericKeys, ...props.series.map((x) => x.propKey)])];

  return (
    <>
      <div className="flex flex-col gap-2">
        {props.series.map((series, index) => (
          <div key={index} className="flex gap-2 items-center">
            <ColorPicker
              color={series.color}
              palette={colors.series}
              onChange={(color) => changeSeries(index, { color })}
            />
            <select
              className="form-select compact min-w-[8rem]"
              value={series.aggregation}
              onChange={(e) => changeSeries(index, { aggregation: e.currentTarget.value as PropertyAggregation })}
            >
              {AGGREGATIONS.map((x) => (
                <option key={x.value} value={x.value}>
                  {x.label}
                </option>
              ))}
            </select>
            <span className="text-sm text-muted-foreground">of</span>
            <select
              className="form-select compact min-w-[8rem]"
              value={series.propKey}
              onChange={(e) => changeSeries(index, { propKey: e.currentTarget.value })}
            >
              {keyOptions.map((key) => (
                <option key={key} value={key}>
                  {key}
                </option>
              ))}
            </select>
            <Button
              variant="ghost"
              size="xs"
              onClick={() =>
                props.onChange(
                  props.series.filter((_, i) => i !== index),
                  props.stackMode
                )
              }
            >
              <IconX className="h-4 w-4" />
            </Button>
          </div>
        ))}
        <Button onClick={addSeries} variant="outline" size="icon" className="rounded-full w-8 h-8">
          <IconPlus className="h-4 w-4" />
        </Button>
      </div>
      <StackModeToggle value={props.stackMode} onChange={(stackMode) => props.onChange(props.series, stackMode)} />
    </>
  );
}

function NumericPropertyChart(props: {
  appId: string;
  eventName: string;
  series: PropertySeries[];
  stackMode: StackMode;
  granularity: Granularity;
}) {
  const { buildMode } = useApps();
  const { startDateIso, endDateIso } = useAtomValue(dateFilterValuesAtom);

  const propKeys = useMemo(() => [...new Set(props.series.map((x) => x.propKey))], [props.series]);
  const results = useQueries({
    queries: propKeys.map((propKey) => ({
      queryKey: [
        "event-prop-numeric",
        buildMode,
        props.appId,
        startDateIso,
        endDateIso,
        props.granularity,
        props.eventName,
        propKey,
      ],
      queryFn: () =>
        eventPropNumeric({
          buildMode,
          appId: props.appId,
          startDate: startDateIso,
          endDate: endDateIso,
          granularity: props.granularity,
          eventName: props.eventName,
          propKey,
        }),
      staleTime: 10000,
      enabled: !!startDateIso && !!endDateIso && !!props.granularity,
    })),
  });

  const isLoading = results.some((x) => x.isLoading);
  const isError = results.some((x) => x.isError);
  let rows = results.map((x) => x.data ?? []);
  if (startDateIso === new Date(0).toISOString()) {
    const firstWithData = Math.min(...rows.map((r) => r.findIndex((x) => x.events > 0)).filter((i) => i >= 0));
    const sliceFrom = Number.isFinite(firstWithData) ? Math.max(0, firstWithData - 2) : 0;
    rows = rows.map((r) => r.slice(sliceFrom));
  }
  const rowsByKey = new Map(propKeys.map((key, index) => [key, rows[index] ?? []]));

  const labels = (rows[0] ?? []).map((x) => x.period);
  const datasets = props.series.map((series) => ({
    label: seriesLabel(series),
    data: (rowsByKey.get(series.propKey) ?? []).map((row) => seriesValue(row, series.aggregation)),
    color: series.color,
    fill: false,
  }));
  const isEmpty = rows.every((r) => r.every((x) => x.events === 0));

  return (
    <LineChart
      labels={labels}
      datasets={datasets}
      stackMode={props.stackMode}
      showLegend
      granularity={props.granularity}
      isEmpty={isEmpty}
      isLoading={isLoading}
      isError={isError}
      formatLabel={(label) => formatPeriod(props.granularity, label.toString())}
      renderTooltip={({ label, points }) => (
        <div className="text-sm whitespace-nowrap">
          <p className="text-center text-muted-foreground">{formatPeriod(props.granularity, label)}</p>
          {points.map((point, index) => (
            <p key={index}>
              <span className="font-medium">{formatNumber(point.value)}</span> {point.name}
              {point.percentage !== undefined && (
                <span className="text-muted-foreground"> ({Math.round(point.percentage * 10) / 10}%)</span>
              )}
            </p>
          ))}
        </div>
      )}
      refetch={async () => (await Promise.all(results.map((x) => x.refetch())))[0]!}
    />
  );
}

type CategoryPropertyChartProps = {
  appId: string;
  eventName: string;
  propKey: string;
  display: CategoryDisplay;
  stackMode: StackMode;
  granularity: Granularity;
};

function CategoryPropertyChart(props: CategoryPropertyChartProps) {
  return props.display === "bar-time" ? <CategoryPropertyTimeChart {...props} /> : <CategoryPropertyTotalsChart {...props} />;
}

function CategoryPropertyTimeChart(props: CategoryPropertyChartProps) {
  const { buildMode } = useApps();
  const { startDateIso, endDateIso } = useAtomValue(dateFilterValuesAtom);

  const { isLoading, isError, data, refetch } = useQuery({
    queryKey: [
      "event-prop-distribution-periodic",
      buildMode,
      props.appId,
      startDateIso,
      endDateIso,
      props.granularity,
      props.eventName,
      props.propKey,
    ],
    queryFn: () =>
      eventPropDistributionPeriodic({
        buildMode,
        appId: props.appId,
        startDate: startDateIso,
        endDate: endDateIso,
        granularity: props.granularity,
        eventName: props.eventName,
        propKey: props.propKey,
      }),
    staleTime: 10000,
    enabled: !!startDateIso && !!endDateIso && !!props.granularity,
  });

  return (
    <CategoryTimeChart
      data={data}
      stackMode={props.stackMode}
      granularity={props.granularity}
      isLoading={isLoading}
      isError={isError}
      refetch={refetch}
    />
  );
}

function CategoryPropertyTotalsChart(props: CategoryPropertyChartProps) {
  const { buildMode } = useApps();
  const { startDateIso, endDateIso } = useAtomValue(dateFilterValuesAtom);

  const { isLoading, isError, data, refetch } = useQuery({
    queryKey: ["event-prop-distribution", buildMode, props.appId, startDateIso, endDateIso, props.eventName, props.propKey],
    queryFn: () =>
      eventPropDistribution({
        buildMode,
        appId: props.appId,
        startDate: startDateIso,
        endDate: endDateIso,
        eventName: props.eventName,
        propKey: props.propKey,
      }),
    staleTime: 10000,
    enabled: !!startDateIso && !!endDateIso,
  });

  return (
    <CategoryChart
      display={props.display}
      items={data?.items ?? []}
      isLoading={isLoading}
      isError={isError}
      refetch={refetch}
    />
  );
}

function ColorPicker(props: { color: string; palette: string[]; onChange: (color: string) => void }) {
  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          className="h-6 w-6 rounded-full border border-border shrink-0"
          style={{ backgroundColor: props.color }}
          aria-label="Change color"
        />
      </PopoverTrigger>
      <PopoverContent className="w-auto p-2">
        <div className="grid grid-cols-5 gap-2">
          {props.palette.map((color) => (
            <button
              key={color}
              className={`h-6 w-6 rounded-full border-2 ${color === props.color ? "border-foreground" : "border-transparent"}`}
              style={{ backgroundColor: color }}
              onClick={() => props.onChange(color)}
              aria-label={color}
            />
          ))}
        </div>
        <label className="flex items-center gap-2 pt-2 text-sm text-muted-foreground">
          Custom
          <input
            type="color"
            value={props.color}
            onChange={(e) => props.onChange(e.currentTarget.value)}
            className="h-6 w-10 cursor-pointer bg-transparent"
          />
        </label>
      </PopoverContent>
    </Popover>
  );
}
